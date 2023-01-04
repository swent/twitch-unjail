using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Utilities;
using Timer = System.Timers.Timer;

namespace TwitchUnjail.Core.Managers {
    
    /**
     * Downloader that takes an array of urls to download and a target filepath
     * to concatenate the downloaded data into.
     * Can use parallelism and a target bandwidth argument to control download speeds.
     */
    public class ChunkedDownloadManager {

        /* Only used if no thread count is given when the start method is called */
        private const int ThreadCount = 6;
        /* Number of retries for a failed download chunk before throwing an exception */
        private const int RetryCount = 5;
        /* Timeframe used to average download and write speeds over */
        private const int SpeedAveragingSeconds = 20;
        /* Timeframe used to average download and write speeds over */
        public const int ProgressUpdateIntervalMilliseconds = 333;
        /* Default speed limit when activated on-the-fly */
        public const int DefaultKbPerSecondSpeedLimit = 10 * 1024;
        /* Maximum speed limit allowed */
        public const int MaximumKbPerSecondSpeedLimit = 60 * 1024;
        /* Minimum speed limit allowed */
        public const int MinimumKbPerSecondSpeedLimit = 512;
        /* Default speed limit when activated on-the-fly */
        public const int DefaultPercentSpeedLimit = 50;
        /* Maximum percent speed limit allowed */
        public const int MaximumPercentSpeedLimit = 75;
        /* Minimum percent speed limit allowed */
        public const int MinimumPercentSpeedLimit = 20;
        /* Interval in which variable speed limit is renewed by measuring speeds */
        public const int BandwidthMeasurementIntervalSeconds = 30;
        /* Timeframe taken to measure download speeds */
        public const int BandwidthMeasurementTimeframeSeconds = 1;
        /* Number of chunks for average chunk time calculation */
        public const int ChunksForChunkSizeCalculation = 50;
        
        /* Event that can be subscribed to for periodic progress updates */
        public delegate void DownloadProgressUpdateHandler(object sender, DownloadProgressUpdateEventArgs eventArgs);
        public event DownloadProgressUpdateHandler DownloadProgressUpdate;
        
        /* The chunks to download */
        public ConcurrentQueue<Chunk> Chunks { get; }
        /* Target filepath where downloaded bytes are written to */
        public string TargetFilePath { get; }

        private int _targetFiles;
        private bool _running;
        private Exception _encounteredException;
        private DownloadManagerProxy _managerProxy;
        private DateTime _startTime;
        private Task _finishedTask;
        private int _finishedIndex;
        private int _lastIndex;
        private ConcurrentDictionary<int, Chunk> _doneQueue;
        private FileStream[] _stream;
        private BinaryWriter[] _writer;
        private Timer _progressTimer;
        private Timer _allowanceTimer;
        private Timer _bandwidthMeasurementTimer;
        private DownloaderSpeedLimit _activeSpeedLimit;
        private int _targetSpeedKbPerSecond;
        private int _targetSpeedPercent;
        private bool _bandwidthMeasurementActive;
        private int _bandwidthMeasurementBytes;
        private int _lastAllowanceIndex;
        private bool _isPaused;
        private ConcurrentDictionary<DateTime, long> _bytesDownloadedTracker;
        private ConcurrentDictionary<DateTime, long> _bytesWrittenTracker;
        private ConcurrentQueue<long> _sizePerChunkQueue;
        private (Thread, MeteredHttpDownloader)[] _threads;
        private FileLogManager _logManager;

        public ChunkedDownloadManager(string[][] urlsToDownload, string targetFilePath, FileLogManager logManager = null) {
            var fileCounter = -1;
            var chunkCounter = 0;
            Chunks = new ConcurrentQueue<Chunk>(urlsToDownload
                .SelectMany(urls => {
                    fileCounter++;
                    return urls
                        .Select(url => new Chunk { FileIndex = fileCounter, Index = chunkCounter++, Url = url });
                }));
            TargetFilePath = targetFilePath;
            _targetFiles = fileCounter + 1;
            _running = false;
            _progressTimer = new Timer();
            _progressTimer.Interval = ProgressUpdateIntervalMilliseconds;
            _progressTimer.AutoReset = true;
            _progressTimer.Elapsed += OnProgressTimerElapsed;
            _bytesDownloadedTracker = new ConcurrentDictionary<DateTime, long>();
            _bytesWrittenTracker = new ConcurrentDictionary<DateTime, long>();
            _sizePerChunkQueue = new ConcurrentQueue<long>();
            _logManager = logManager;
            _logManager.LogIfAvailable($"[CDM] Initialized {Chunks.Count} chunks over {fileCounter} files");
        }

        /**
         * Starts the download on the chunks.
         * Allows specifying the thread count and target speed.
         * Optionally a progress tracker can be injected that will be used to proxy
         * periodic update information to a caller.
         */
        public async ValueTask Start(int? threadCount = null, int? targetKbps = null,
            DownloadManagerProxy managerProxy = null) {
            if (_running) {
                throw new Exception("Download manager already running.");
            }
            var threads = threadCount ?? ThreadCount;
            _logManager.LogIfAvailable($"[CDM] Start()");
            _logManager.LogIfAvailable($"[CDM]     thread count: {threads}");
            _logManager.LogIfAvailable($"[CDM]     using speed limit: {targetKbps != null}");
            /* Cleanup and preset before run */
            _running = true;
            _managerProxy = managerProxy;
            _startTime = DateTime.Now;
            _finishedTask = new Task(() => {});
            _encounteredException = null;
            _finishedIndex = -1;
            _lastIndex = Chunks.Count - 1;
            _doneQueue = new ConcurrentDictionary<int, Chunk>();
            _isPaused = false;
            _activeSpeedLimit = DownloaderSpeedLimit.None;
            _bandwidthMeasurementActive = false;
            Directory.CreateDirectory(Path.GetDirectoryName(TargetFilePath)!);
            _stream = new FileStream[_targetFiles];
            _writer = new BinaryWriter[_targetFiles];
            for (var i = 0; i < _targetFiles; i++) {
                var filename = GetPartedFilename(i);
                _logManager.LogIfAvailable($"[CDM] Initializing stream and writer for file '{filename}' at index {i} ...");
                _stream[i] = new FileStream(filename, FileMode.Create);
                _writer[i] = new BinaryWriter(_stream[i], Encoding.UTF8);
            }

            /* Start threads and do work */
            _threads = Enumerable.Range(0, threads)
                .Select(i => (new Thread(DoWork),
                    new MeteredHttpDownloader(i, null, _logManager)))
                .ToArray();
            if (targetKbps != null) {
                EnableFixedSpeedLimit((int)targetKbps);
            }
            for (var i = 0; i < _threads.Length; i++) {
                _threads[i].Item2.DownloadNotification += OnThreadDownloadNotification;
                _threads[i].Item1.Start(i);
            }
            if (managerProxy != null) {
                _progressTimer.Start();
                managerProxy.DownloaderCommand += OnProxyDownloaderCommand;
            }

            /* Cleanup after run */
            _logManager.LogIfAvailable($"[CDM] Awaiting finish task ...");
            await _finishedTask;
            _logManager.LogIfAvailable($"[CDM] Finish task completed, download ended !");
            if (managerProxy != null) {
                _progressTimer.Stop();
                managerProxy.DownloaderCommand -= OnProxyDownloaderCommand;
            }
            for (var i = 0; i < _targetFiles; i++) {
                _logManager.LogIfAvailable($"[CDM] Disposing stream and writer at index {i} ...");
                await _writer[i].DisposeAsync();
                await _stream[i].DisposeAsync();
            }
            _allowanceTimer?.Stop();
            for (var i = 0; i < _threads.Length; i++) {
                _threads[i].Item2.DownloadNotification -= OnThreadDownloadNotification;
            }
            _running = false;

            /* Check for errors */
            if (_encounteredException != null) {
                throw _encounteredException;
            }
        }

        /**
         * Starts the timer that is used to periodically distribute the byte allowance per thread.
         */
        private void StartAllowanceTimer() {
            if (_allowanceTimer == null) {
                _allowanceTimer = new Timer();
                _allowanceTimer.Interval = 1000.0 / _threads.Length;
                _allowanceTimer.AutoReset = true;
                _allowanceTimer.Elapsed += OnAllowanceTimerElapsed;
            }
            if (!_allowanceTimer.Enabled) {
                _logManager.LogIfAvailable($"[CDM] AllowanceTimer started !");
                _allowanceTimer.Start();
            }
        }
        
        /**
         * Starts the timer that is used to periodically measure the available bandwidth.
         */
        private void StartBandwidthMeasurementTimer() {
            if (_bandwidthMeasurementTimer == null) {
                _bandwidthMeasurementTimer = new Timer();
                _bandwidthMeasurementTimer.Interval = BandwidthMeasurementIntervalSeconds * 1000.0;
                _bandwidthMeasurementTimer.AutoReset = false;
                _bandwidthMeasurementTimer.Elapsed += OnBandwidthMeasurementTimerElapsed;
            }
            if (!_bandwidthMeasurementTimer.Enabled) {
                _logManager.LogIfAvailable($"[CDM] BandwidthMeasurementTimer started !");
                _bandwidthMeasurementTimer.Start();
            }
        }
        
        /**
         * Adjusts the stored filename to include a number for multiple target files.
         */
        private string GetPartedFilename(int fileIndex) {
            if (_targetFiles == 1) {
                return TargetFilePath;
            }
            var parts = TargetFilePath.Split('.');
            return string.Join(".", parts.Take(parts.Length - 1)) + $".{fileIndex + 1}.{parts.Last()}";
        }

        /**
         * Main method for download worker threads.
         * Gets called using an index that allows each thread to access
         * their metered downloader sub-class.
         */
        private async void DoWork(object idx) {
            int threadIndex = (int)idx;
            _logManager.LogIfAvailable($"[CDM] Thread {threadIndex} starting up ...");
            var downloader = _threads[threadIndex].Item2;
            var stopWatch = new Stopwatch();

            /* Process until chunks-queue is empty */
            while (_encounteredException == null && Chunks.TryDequeue(out var chunk)) {

                /* Loop until chunk is successfully done */
                _logManager.LogIfAvailable($"[CDM] Thread {threadIndex} picked chunk {chunk.Index} from queue");
                var retryCounter = 0;
                while (!chunk.Done) {
                    try {
                        stopWatch.Restart();
                        _logManager.LogIfAvailable($"[CDM] Thread {threadIndex} starting attempt {retryCounter + 1} to download chunk {chunk.Index} ...");
                        chunk.Content = await downloader.Download(chunk.Url);
                        _logManager.LogIfAvailable($"[CDM] Thread {threadIndex} finished downloading chunk {chunk.Index} !");
                        stopWatch.Stop();
                        _sizePerChunkQueue.Enqueue(chunk.Content.Length);
                        MarkFinished(chunk);
                    } catch (Exception ex) {
                        retryCounter++;
                        /* Kill all download threads and exit out if too many failed attempts */
                        _logManager.LogIfAvailable($"[CDM] Thread {threadIndex} encountered an exception when downloading chunk {chunk.Index}: {ex.Message} {ex.StackTrace}");
                        if (retryCounter > RetryCount) {
                            if (ex is HttpRequestException && ex.Message.Contains("403 (Forbidden)")) {
                                AbortThreads(
                                    new Exception(
                                        "Twitch server responded status code 403, most likely the vod has been deleted from twitch servers. Nothing we can do about it."));
                            } else {
                                AbortThreads(ex);
                            }
                            return;
                        } else {
                            /* Wait a little before retrying the same chunk that just failed */
                            Thread.Sleep(5 * 1000);
                        }
                    }
                }
            }
        }

        /**
         * Marks a given chunk as finished.
         * Is executed on any of the worker threads that finished downloading the chunk.
         * Will push the chunk to the done-queue and then continue processing all entries
         * on the done-queue if this is the chunk that is currently being waited for.
         * Any other thread with a chunk that has a higher index than what is currently
         * being waited for will just drop the chunk on the queue and go back to
         * downloading.
         */
        private void MarkFinished(Chunk chunk) {
            _doneQueue[chunk.Index] = chunk;
            chunk.Done = true;
            
            /* Check if this thread passed in the next chunk we are waiting for, if so go into write logic */
            if (chunk.Index == _finishedIndex + 1) {
                _logManager.LogIfAvailable($"[CDM] Chunk {chunk.Index} is finished and next in order ! Starting to write all available chunks from queue ...");
                var counter = _finishedIndex + 1;
                while (_doneQueue.TryGetValue(counter, out var writeChunk)) {
                    _logManager.LogIfAvailable($"[CDM] Chunk {writeChunk.Index} of size {writeChunk.Content?.Length ?? -1} bytes is being written to disk ...");
                    _finishedIndex = counter - 1;
                    _writer[writeChunk.FileIndex].Write(writeChunk.Content);
                    _logManager.LogIfAvailable($"[CDM] Chunk {writeChunk.Index} writing completed !");
                    _bytesWrittenTracker[DateTime.Now] = writeChunk.Content.Length;
                    if (_doneQueue.Remove(writeChunk.Index, out var temp)) {
                        temp.Content = null;
                    }
                    counter++;
                }
                _finishedIndex = counter - 1;
                if (counter > _lastIndex) {
                    /* Signal that we're done */
                    _logManager.LogIfAvailable($"[CDM] All {counter} chunks finished, signaling finish task to resume ...");
                    _finishedTask?.RunSynchronously();
                }
            } else {
                /* Slow thread down a little if queue is building up too fast */
                _logManager.LogIfAvailable($"[CDM] Chunk {chunk.Index} is finished but not next in order ({_finishedIndex + 1}), placing on queue ...");
                var queueFactor = _doneQueue.Count / (double)ThreadCount;
                if (queueFactor >= 6) {
                    Thread.Sleep(60 * 1000);
                } else if (queueFactor >= 4.0) {
                    Thread.Sleep(30 * 1000);
                } else if (queueFactor >= 3.0) {
                    Thread.Sleep(15 * 1000);
                } else if (queueFactor >= 2.0) {
                    Thread.Sleep(5 * 1000);
                }
            }
        }

        /**
         * Sets the exception private field that is checked by all worker threads
         * and causes them to quit eventually.
         * When all other threads have exited, will signal the main download method
         * to resume.
         */
        public void AbortThreads(Exception exception) {
            /* Set exception property and wait for all threads to exit */
            if (_encounteredException == null) {
                _logManager.LogIfAvailable($"[CDM] Aborting all threads due to exception: {exception.Message}");
                _encounteredException = exception;
                int aliveCount;
                while ((aliveCount = _threads.Count(t => t.Item1.IsAlive)) > 1) {
                    _logManager.LogIfAvailable($"[CDM] Waiting for threads to end (alive: {aliveCount}) ...");
                    Thread.Sleep(500);
                }
            
                /* Signal main method to continue */
                _logManager.LogIfAvailable($"[CDM] All {_threads.Length} threads stopped, signaling finish task to resume ...");
                _finishedTask?.RunSynchronously();
            }
        }

        /**
         * Pauses the running download.
         * Pausing for too long might trigger a re-download of partially finished segments upon resume.
         */
        public void PauseDownload() {
            if (_isPaused) {
                throw new Exception("Download is already paused.");
            }

            _logManager.LogIfAvailable($"[CDM] Pausing download ...");
            foreach (var thread in _threads) {
                thread.Item2.Pause();
            }
            _isPaused = true;
        }
        
        /**
         * Resumes the paused download.
         * Pausing for too long might trigger a re-download of partially finished segments upon resume.
         */
        public void ResumeDownload() {
            if (!_isPaused) {
                throw new Exception("Download is already paused.");
            }
            
            _logManager.LogIfAvailable($"[CDM] Resuming download ...");
            foreach (var thread in _threads) {
                thread.Item2.Resume();
            }
            _isPaused = false;
        }
        
        /**
         * Sets a fixed speed limit for the download.
         */
        public void EnableFixedSpeedLimit(int targetKbPerSecond) {
            _logManager.LogIfAvailable($"[CDM] Enabling fixed speed limit ...");
            _activeSpeedLimit = DownloaderSpeedLimit.Fixed;
            foreach (var thread in _threads) {
                thread.Item2.SetByteAllowance(targetKbPerSecond / _threads.Length * 1024 / 8);
            }
            StartAllowanceTimer();
            _bandwidthMeasurementTimer?.Stop();
            _targetSpeedKbPerSecond = targetKbPerSecond;
            _lastAllowanceIndex = -1;
            _bandwidthMeasurementActive = false;
        }
        
        /**
         * Sets a variable speed limit for the download.
         */
        public void EnableVariableSpeedLimit(int targetPercent) {
            _logManager.LogIfAvailable($"[CDM] Enabling variable speed limit ...");
            _activeSpeedLimit = DownloaderSpeedLimit.Variable;
            var currentDownloadSpeedKbPerSecond = (int)(CalculateRecentSpeeds()[0] * (targetPercent / 100.0));
            foreach (var thread in _threads) {
                thread.Item2.SetByteAllowance(currentDownloadSpeedKbPerSecond / _threads.Length * 1024 / 8);
            }
            StartAllowanceTimer();
            StartBandwidthMeasurementTimer();
            _targetSpeedKbPerSecond = currentDownloadSpeedKbPerSecond;
            _targetSpeedPercent = targetPercent;
            _lastAllowanceIndex = -1;
            _bandwidthMeasurementActive = false;
        }
        
        /**
         * Disables any speed limit for the download.
         */
        public void DisableSpeedLimit() {
            _logManager.LogIfAvailable($"[CDM] Disabling speed limit ...");
            _activeSpeedLimit = DownloaderSpeedLimit.None;
            foreach (var thread in _threads) {
                thread.Item2.SetByteAllowance(null);
            }
            _allowanceTimer.Stop();
            _bandwidthMeasurementTimer?.Stop();
            _targetSpeedKbPerSecond = -1;
            _bandwidthMeasurementActive = false;
        }
        
        /**
         * Called periodically by the progress timer.
         * Will fire the progress update event and instruct the injected progress tracker
         * to signal an update.
         */
        private void OnProgressTimerElapsed(object sender, ElapsedEventArgs e) {
            var eventArgs = GenerateProgressUpdateEventArgs();
            DownloadProgressUpdate?.Invoke(this, eventArgs);
            if (_managerProxy != null) {
                _managerProxy.SignalProgressUpdate(eventArgs);
            }
        }
        
        /**
         * Called periodically by the allowance timer.
         * Each time this method is called, it will grant allowance to the next thread,
         * cycling all threads until stopped.
         */
        private void OnAllowanceTimerElapsed(object sender, ElapsedEventArgs e) {
            var allowanceIndex = ++_lastAllowanceIndex % _threads.Length;
            var bytesGranted = (int)(_targetSpeedKbPerSecond * 1024.0 / _threads.Length *
                                     (_activeSpeedLimit == DownloaderSpeedLimit.Fixed
                                         ? 1.0
                                         : 1.0 - (double)BandwidthMeasurementTimeframeSeconds /
                                         BandwidthMeasurementIntervalSeconds / 2));
            _logManager.LogIfAvailable($"[CDM] Granting {bytesGranted} bytes of allowance to thread {allowanceIndex} ...");
            _threads[allowanceIndex].Item2.Grant(bytesGranted);
            _lastAllowanceIndex = allowanceIndex;
        }
        
        /**
         * Called periodically by the bandwidth measurement timer.
         * Will initiate bandwidth measurement and afterwards set all threads to a new dynamic
         * download speed based on the measurement results.
         */
        private void OnBandwidthMeasurementTimerElapsed(object sender, ElapsedEventArgs e) {
            if (!_bandwidthMeasurementActive) {
                /* Start measuring throughput */
                _bandwidthMeasurementBytes = 0;
                _bandwidthMeasurementActive = true;
                /* Temporarily stop allowance and unlimit all threads */
                _allowanceTimer.Stop();
                for (var i = 0; i < _threads.Length; i++) {
                    _threads[i].Item2.SetByteAllowance(null);
                }
                /* Schedule a new timer tick to calculate the results of this test */
                _bandwidthMeasurementTimer.Interval = BandwidthMeasurementTimeframeSeconds * 1000.0;
                _bandwidthMeasurementTimer.Start();
            } else {
                /* Stop measuring */
                _bandwidthMeasurementActive = false;
                var bytesPerSecond = _bandwidthMeasurementBytes / BandwidthMeasurementTimeframeSeconds;
                /* Use a dirty simple metric to decrease the measurement if many threads are used since
                 they each already downloaded a part that will be reported to the measurement before measuring
                 even began. Also use the chunk size to counter-weight the error-adjust. Hopefully this can be
                 replaced by some proper metric at some point. */
                var chunkSizeAdjustmentFactor = Math.Min(0.5 + Math.Min(GetAverageChunkSize() / 1024 / 1024, 5.3) * 0.0943, 1.0);
                var errorAdjustmentFactor = Math.Max((1.06 + 0.021 * _threads.Length) * Math.Pow(1.021, _threads.Length) * chunkSizeAdjustmentFactor /*+ (100.0 / _targetSpeedPercent - 1.33) * 0.03*/, 1.0);
                /* Apply new speed limit */
                _targetSpeedKbPerSecond = (int)(bytesPerSecond / 1024.0 * (_targetSpeedPercent / 100.0) / errorAdjustmentFactor);
                var allowancePerThread = (int)(_targetSpeedKbPerSecond * 1024.0 / _threads.Length / 16);
                /* Set all threads to limited allowance again */
                for (var i = 0; i < _threads.Length; i++) {
                    _threads[i].Item2.SetByteAllowance(allowancePerThread);
                }
                StartAllowanceTimer();
                /* Plan next measurement */
                _bandwidthMeasurementTimer.Interval = BandwidthMeasurementIntervalSeconds * 1000.0;
                _bandwidthMeasurementTimer.Start();
            }
        }

        /**
         * Called when a {MeteredHttpDownloader} class has read some bytes from the
         * download stream so this class can track the download speed.
         */
        private void OnThreadDownloadNotification(object sender, int threadIndex, int bytesDownloaded) {
            _bytesDownloadedTracker[DateTime.Now] = bytesDownloaded;

            if (_bandwidthMeasurementActive) {
                _bandwidthMeasurementBytes += bytesDownloaded;
            }
        }
        
        /**
         * Executed when the download proxy invokes commands.
         */
        private void OnProxyDownloaderCommand(object sender, DownloaderCommand command) {
            _logManager.LogIfAvailable($"[CDM] Downloader command received: {command}");
            
            switch (command) {
                
                case DownloaderCommand.TogglePause:
                    if (_isPaused) ResumeDownload();
                    else PauseDownload();
                    return;
                
                case DownloaderCommand.ToggleFixedSpeedLimit:
                    if (_activeSpeedLimit == DownloaderSpeedLimit.Fixed) {
                        DisableSpeedLimit();
                    } else {
                        EnableFixedSpeedLimit(DefaultKbPerSecondSpeedLimit);
                    }
                    return;
                
                case DownloaderCommand.ToggleVariableSpeedLimit:
                    if (_activeSpeedLimit == DownloaderSpeedLimit.Variable) {
                        DisableSpeedLimit();
                    } else {
                        EnableVariableSpeedLimit(DefaultPercentSpeedLimit);
                    }
                    return;
                
                case DownloaderCommand.IncreaseSpeedLimit:
                    if (_activeSpeedLimit == DownloaderSpeedLimit.Fixed) {
                        _logManager.LogIfAvailable($"[CDM] Increasing fixed speed limit ...");
                        _targetSpeedKbPerSecond = (int)(_targetSpeedKbPerSecond * 1.12);
                        if (_targetSpeedKbPerSecond > MaximumKbPerSecondSpeedLimit) {
                            DisableSpeedLimit();
                        }
                    } else if (_activeSpeedLimit == DownloaderSpeedLimit.Variable) {
                        _logManager.LogIfAvailable($"[CDM] Increasing variable speed limit ...");
                        _targetSpeedPercent = Math.Min(_targetSpeedPercent + 5, MaximumPercentSpeedLimit);
                    }
                    return;
                
                case DownloaderCommand.DecreaseSpeedLimit:
                    if (_activeSpeedLimit == DownloaderSpeedLimit.Fixed) {
                        _logManager.LogIfAvailable($"[CDM] Decreasing speed limit ...");
                        _targetSpeedKbPerSecond = Math.Max((int)(_targetSpeedKbPerSecond / 1.12), MinimumKbPerSecondSpeedLimit);
                    } else if (_activeSpeedLimit == DownloaderSpeedLimit.Variable) {
                        _logManager.LogIfAvailable($"[CDM] Decreasing variable speed limit ...");
                        _targetSpeedPercent = Math.Max(_targetSpeedPercent - 5, MinimumPercentSpeedLimit);
                    }
                    return;
            }
        }

        /**
         * Generates download progress update event args containing many useful metrics.
         */
        private DownloadProgressUpdateEventArgs GenerateProgressUpdateEventArgs() {
            var total = _lastIndex + 1;
            var written = _finishedIndex + 1;
            var downloaded = written + _doneQueue.Count;

            var speeds = CalculateRecentSpeeds();
            var secondsElapsed = (DateTime.Now - _startTime).TotalSeconds;
            return new DownloadProgressUpdateEventArgs {
                TargetFile = TargetFilePath,
                SecondsElapsed = secondsElapsed,
                ChunksTotal = total,
                ChunksDownloaded = downloaded,
                ChunksWritten = written,
                DownloadSpeedKBps = speeds[0],
                WriteSpeedKBps = speeds[1],
                IsPaused = _isPaused,
                TargetSpeedLimitKbPerSecond = _activeSpeedLimit != DownloaderSpeedLimit.None ? _targetSpeedKbPerSecond : null,
                TargetSpeedPercent = _activeSpeedLimit == DownloaderSpeedLimit.Variable ? _targetSpeedPercent : null,
                IsMeasuringSpeed = _activeSpeedLimit == DownloaderSpeedLimit.Variable && _bandwidthMeasurementActive,
            };
        }
        
        private int[] CalculateRecentSpeeds() {
            /* Calculate download speed */
            var downloadEntries = _bytesDownloadedTracker.ToArray();
            long bytesDownloaded = 0;
            foreach (var entry in downloadEntries) {
                if ((DateTime.Now - entry.Key).TotalSeconds <= SpeedAveragingSeconds) {
                    bytesDownloaded += entry.Value;
                } else {
                    _bytesDownloadedTracker.Remove(entry.Key, out _);
                }
            }
            
            /* Calculate write speed */
            var writtenEntries = _bytesWrittenTracker.ToArray();
            long bytesWritten = 0;
            foreach (var entry in writtenEntries) {
                if ((DateTime.Now - entry.Key).TotalSeconds <= SpeedAveragingSeconds) {
                    bytesWritten += entry.Value;
                } else {
                    _bytesWrittenTracker.Remove(entry.Key, out _);
                }
            }
            
            var secondsElapsed = (DateTime.Now - _startTime).TotalSeconds;
            var averageOverSeconds = Math.Min(secondsElapsed, SpeedAveragingSeconds);
            return new[] {
                (int)(bytesDownloaded / 1024.0 / averageOverSeconds),
                (int)(bytesWritten / 1024.0 / averageOverSeconds),
            };
        }

        private double GetAverageChunkSize() {
            while (_sizePerChunkQueue.Count > ChunksForChunkSizeCalculation) {
                _sizePerChunkQueue.TryDequeue(out _);
            }
            return _sizePerChunkQueue.Average();
        }
    }

    /**
     * Micro downloader class that uses a byte buffer and streams to to read downloads.
     * Can be set to an byte allowance to limit the number of bytes read and therefore
     * limiting the download speed.
     */
    public class MeteredHttpDownloader {

        /* Max size that can be read in one read-operation */
        private const int BufferSize = 64 * 1024;
        /* Number of bytes that can be saved up, e.g. while the thread is rather writing the result file */
        private const int MaxSavedUpAllowance = 16 * 1024 * 1024;
        
        /* Events used to update subscribers of number of bytes downloaded */
        public delegate void DownloadNotificationHandler(object sender, int threadIndex, int bytesDownloaded);
        public event DownloadNotificationHandler DownloadNotification;

        private HttpClient _client;
        private int _threadIndex;
        private int? _byteAllowance;
        private byte[] _buffer;
        private object _lock;
        private bool _isPaused;
        private FileLogManager _logManager;
        
        public MeteredHttpDownloader(int threadIndex, int? byteAllowance, FileLogManager logManager = null) {
            _client = new HttpClient();
            _threadIndex = threadIndex;
            _client.DefaultRequestHeaders.Add("User-Agent", HttpHelper.UserAgent);
            _byteAllowance = byteAllowance;
            _buffer = new byte[BufferSize];
            _lock = new object();
            _isPaused = false;
            _logManager = logManager;
        }

        /**
         * Sets the allowance to a specified amount or null.
         */
        public void SetByteAllowance(int? bytes) {
            lock (_lock) {
                _byteAllowance = bytes;
            }
        }

        /**
         * Grands a number of bytes to this downloaders allowance.
         */
        public void Grant(int bytes) {
            lock (_lock) {
                _byteAllowance = Math.Min((_byteAllowance ?? 0) + bytes, MaxSavedUpAllowance);
            }
        }

        /**
         * Starts downloading the given url.
         * If an allowance is set, the thread will be put to sleep for a short time
         * whenever it runs out of allowance.
         */
        public async ValueTask<byte[]> Download(string url) {
            /* Send GET and await response with headers */
            _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} requesting url '{url}' ...");
            var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} got response, code {response.StatusCode} ...");
            response.EnsureSuccessStatusCode();
            
            int bytesRead;
            var chunks = new List<byte[]>();
            
            /* Start processing data */
            using (var stream = await response.Content.ReadAsStreamAsync()) {
                while (true) {
                    /* If allowance is active, wait for bytes to become available */
                    if (_isPaused || _byteAllowance != null) {
                        while (_isPaused || _byteAllowance <= 0) {
                            _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} is paused ({_isPaused}) or waiting for allowance ({_byteAllowance is 0}) ...");
                            Thread.Sleep(100);
                        }
                    }

                    /* Determine number of bytes to read */
                    int bytesToRead;
                    lock (_lock) {
                        bytesToRead = Math.Min(BufferSize, _byteAllowance != null ? (int)_byteAllowance : int.MaxValue);
                    }

                    /* Read bytes and add as one chunk */
                    _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} trying to read {bytesToRead} bytes ...");
                    bytesRead = await stream.ReadAsync(_buffer, 0, bytesToRead);
                    
                    _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} got {bytesRead} bytes !");
                    if (bytesRead == 0) break; /* Exit reading loop if no bytes could be read */
                    DownloadNotification?.Invoke(this, _threadIndex, bytesRead);
                    var chunk = new byte[bytesRead];
                    for (var i = 0; i < bytesRead; i++) {
                        chunk[i] = _buffer[i];
                    }
                    chunks.Add(chunk);
                    
                    /* Adjust allowance */
                    if (_byteAllowance != null) {
                        lock (_lock) {
                            _byteAllowance = _byteAllowance - bytesRead;
                        }
                    }
                }
            }

            /* Concat chunks into one byte array */
            var totalBytes = chunks.Aggregate(0, (res, cur) => res + cur.Length);
            var result = new byte[totalBytes];
            var counter = 0;
            _logManager.LogIfAvailable($"[MHD] Thread {_threadIndex} finalizing download, merging {chunks.Count} parts of {totalBytes} bytes ...");
            foreach (var chunk in chunks) {
                for (var i = 0; i < chunk.Length; i++) {
                    result[counter + i] = chunk[i];
                }
                counter += chunk.Length;
            }
            return result;
        }

        /**
         * Pauses this downloader until resume is called.
         */
        public void Pause() {
            _isPaused = true;
        }
        
        /**
         * Resumes this downloader.
         */
        public void Resume() {
            _isPaused = false;
        }
        
        ~MeteredHttpDownloader() {
            _client.Dispose();
        }
    }

    /**
     * Chunk model for the downloader.
     */
    public class Chunk {
        public int FileIndex { get; set; }
        public int Index { get; set; }
        public string Url { get; set; }
        public byte[] Content { get; set; }
        public bool Done { get; set; }
    }

    /**
     * Progress updates event model.
     */
    public class DownloadProgressUpdateEventArgs {
        public string TargetFile { get; set; }
        public double SecondsElapsed { get; set; }
        public int ChunksTotal { get; set; }
        public int ChunksDownloaded { get; set; }
        public int ChunksWritten { get; set; }
        public int DownloadSpeedKBps { get; set; }
        public int WriteSpeedKBps { get; set; }
        public bool IsPaused { get; set; }
        public int? TargetSpeedLimitKbPerSecond { get; set; }
        
        public int? TargetSpeedPercent { get; set; }
        
        public bool IsMeasuringSpeed { get; set; }
    }

    public enum DownloaderCommand {
        TogglePause,
        ToggleFixedSpeedLimit,
        ToggleVariableSpeedLimit,
        IncreaseSpeedLimit,
        DecreaseSpeedLimit,
    }

    public enum DownloaderSpeedLimit {
        None,
        Fixed,
        Variable,
    }
}
