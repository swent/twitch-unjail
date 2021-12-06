using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private DownloadProgressTracker _progressTracker;
        private DateTime _startTime;
        private Task _finishedTask;
        private int _finishedIndex;
        private int _lastIndex;
        private ConcurrentDictionary<int, Chunk> _doneQueue;
        private FileStream[] _stream;
        private BinaryWriter[] _writer;
        private Timer _progressTimer;
        private Timer _allowanceTimer;
        private int _targetKbps;
        private int _lastAllowanceIndex;
        private ConcurrentDictionary<DateTime, long> _bytesDownloadedTracker;
        private ConcurrentDictionary<DateTime, long> _bytesWrittenTracker;
        private (Thread, MeteredHttpDownloader)[] _threads;

        public ChunkedDownloadManager(string[][] urlsToDownload, string targetFilePath) {
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
            _progressTimer.Interval = 400.0;
            _progressTimer.AutoReset = true;
            _progressTimer.Elapsed += OnProgressTimerElapsed;
            _bytesDownloadedTracker = new ConcurrentDictionary<DateTime, long>();
            _bytesWrittenTracker = new ConcurrentDictionary<DateTime, long>();
        }

        /**
         * Starts the download on the chunks.
         * Allows specifying the thread count and target speed.
         * Optionally a progress tracker can be injected that will be used to proxy
         * periodic update information to a caller.
         */
        public async ValueTask Start(int? threadCount = null, int? targetKbps = null, DownloadProgressTracker progressTracker = null) {
            if (_running) {
                throw new Exception("Download manager already running.");
            }
            /* Cleanup and preset before run */
            var threads = threadCount ?? ThreadCount;
            _running = true;
            _progressTracker = progressTracker;
            _startTime = DateTime.Now;
            _finishedTask = new Task(() => {});
            _encounteredException = null;
            _finishedIndex = -1;
            _lastIndex = Chunks.Count - 1;
            _doneQueue = new ConcurrentDictionary<int, Chunk>();
            Directory.CreateDirectory(Path.GetDirectoryName(TargetFilePath)!);
            _stream = new FileStream[_targetFiles];
            _writer = new BinaryWriter[_targetFiles];
            for (var i = 0; i < _targetFiles; i++) {
                _stream[i] = new FileStream(GetPartedFilename(i), FileMode.Create);
                _writer[i] = new BinaryWriter(_stream[i], Encoding.UTF8);
            }
            if (targetKbps != null) {
                StartAllowanceTimer(threads);
                _targetKbps = (int)targetKbps;
                _lastAllowanceIndex = -1;
            }

            /* Start threads and do work */
            _threads = Enumerable.Range(0, threads)
                .Select(i => (new Thread(DoWork), new MeteredHttpDownloader(targetKbps == null ? null : _targetKbps / threads * 1024 / 4)))
                .ToArray();
            for (var i = 0; i < _threads.Length; i++) {
                _threads[i].Item2.DownloadNotification += OnThreadDownloadNotification;
                _threads[i].Item1.Start(i);
            }
            if (progressTracker != null)
                _progressTimer.Start();

            /* Cleanup after run */
            await _finishedTask;
            if (progressTracker != null) {
                _progressTimer.Stop();
            }
            for (var i = 0; i < _targetFiles; i++) {
                await _writer[i].DisposeAsync();
                await _stream[i].DisposeAsync();
            }
            if (targetKbps != null) {
                _allowanceTimer.Stop();
            }
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
        private void StartAllowanceTimer(int threadCount) {
            _allowanceTimer = new Timer();
            _allowanceTimer.Interval = 1000 / threadCount; /* To account for int-cutoff and timer delay we set the interval a little faster than 1sec */
            _allowanceTimer.AutoReset = true;
            _allowanceTimer.Elapsed += OnAllowanceTimerElapsed;
            _allowanceTimer.Start();
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
        private async void DoWork(object threadIndex) {
            var downloader = _threads[(int)threadIndex].Item2;

            /* Process until chunks-queue is empty */
            while (_encounteredException == null && Chunks.TryDequeue(out var chunk)) {
                
                /* Loop until chunk is successfully done */
                var retryCounter = 0;
                while (!chunk.Done) {
                    try {
                        chunk.Content = await downloader.Download(chunk.Url);
                        MarkFinished(chunk);
                    } catch (Exception ex) {
                        retryCounter++;
                        /* Kill all download threads and exit out if too many failed attempts */
                        if (retryCounter > RetryCount) {
                            AbortThreads(ex);
                            return;
                        } else {
                            /* Wait a little before retrying the same chunk that just failed */
                            Thread.Sleep(10 * 1000);
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
                var counter = _finishedIndex + 1;
                while (_doneQueue.TryGetValue(counter, out var writeChunk)) {
                    _finishedIndex = counter - 1;
                    _writer[writeChunk.FileIndex].Write(writeChunk.Content);
                    _bytesWrittenTracker[DateTime.Now] = writeChunk.Content.Length;
                    if (_doneQueue.Remove(writeChunk.Index, out var temp)) {
                        temp.Content = null;
                    }
                    counter++;
                }
                _finishedIndex = counter - 1;
                if (counter > _lastIndex) {
                    /* Signal that we're done */
                    _finishedTask?.RunSynchronously();
                }
            } else {
                /* Slow thread down a little if queue is building up too fast */
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
            _encounteredException = exception;
            while (_threads.Count(t => t.Item1.IsAlive) > 1) {
                Thread.Sleep(500);
            }
            
            /* Signal main method to continue */
            _finishedTask?.RunSynchronously();
        }
        
        /**
         * Called periodically by the progress timer.
         * Will fire the progress update event and instruct the injected progress tracker
         * to signal an update.
         */
        private void OnProgressTimerElapsed(object sender, ElapsedEventArgs e) {
            var eventArgs = GenerateProgressUpdateEventArgs();
            DownloadProgressUpdate?.Invoke(this, eventArgs);
            if (_progressTracker != null) {
                _progressTracker.SignalProgressUpdate(eventArgs);
            }
        }
        
        /**
         * Called periodically by the allowance timer.
         * Each time this method is called, it will grant allowance to the next thread,
         * cycling all threads until stopped.
         */
        private void OnAllowanceTimerElapsed(object sender, ElapsedEventArgs e) {
            var allowanceIndex = ++_lastAllowanceIndex % _threads.Length;
            _threads[allowanceIndex].Item2.Grant(_targetKbps * 1024 / _threads.Length);
            _lastAllowanceIndex = allowanceIndex;
        }

        /**
         * Called when a {MeteredHttpDownloader} class has read some bytes from the
         * download stream so this class can track the download speed.
         */
        private void OnThreadDownloadNotification(object sender, int bytesDownloaded) {
            _bytesDownloadedTracker[DateTime.Now] = bytesDownloaded;
        }

        /**
         * Generates download progress update event args containing many useful metrics.
         */
        private DownloadProgressUpdateEventArgs GenerateProgressUpdateEventArgs() {
            var total = _lastIndex + 1;
            var written = _finishedIndex + 1;
            var downloaded = written + _doneQueue.Count;

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
            return new DownloadProgressUpdateEventArgs {
                TargetFile = TargetFilePath,
                SecondsElapsed = secondsElapsed,
                ChunksTotal = total,
                ChunksDownloaded = downloaded,
                ChunksWritten = written,
                DownloadSpeedKBps = (int)(bytesDownloaded / 1024.0 / averageOverSeconds),
                WriteSpeedKBps = (int)(bytesWritten / 1024.0 / averageOverSeconds),
            };
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
        public delegate void DownloadNotificationHandler(object sender, int bytesDownloaded);
        public event DownloadNotificationHandler DownloadNotification;

        private HttpClient _client;
        private int? _byteAllowance;
        private byte[] _buffer;
        private object _lock;
        
        public MeteredHttpDownloader(int? byteAllowance) {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", HttpHelper.UserAgent);
            _byteAllowance = byteAllowance;
            _buffer = new byte[BufferSize];
            _lock = new object();
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
         * if runs out of allowance.
         */
        public async ValueTask<byte[]> Download(string url) {
            /* Send GET and await response with headers */
            var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            int bytesRead;
            var chunks = new List<byte[]>();
            
            /* Start processing data */
            using (var stream = await response.Content.ReadAsStreamAsync()) {
                while (true) {
                    /* If allowance is active, wait for bytes to become available */
                    if (_byteAllowance != null) {
                        while (_byteAllowance == 0) {
                            Thread.Sleep(100);
                        }
                    }

                    /* Determine number of bytes to read */
                    int bytesToRead;
                    lock (_lock) {
                        bytesToRead = Math.Min(BufferSize, _byteAllowance != null ? (int)_byteAllowance : int.MaxValue);
                    }

                    /* Read bytes and add as one chunk */
                    bytesRead = await stream.ReadAsync(_buffer, 0, bytesToRead);
                    if (bytesRead == 0) break; /* Exit reading loop if no bytes could be read */
                    DownloadNotification?.Invoke(this, bytesRead);
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
            foreach (var chunk in chunks) {
                for (var i = 0; i < chunk.Length; i++) {
                    result[counter + i] = chunk[i];
                }
                counter += chunk.Length;
            }
            return result;
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
    }
}
