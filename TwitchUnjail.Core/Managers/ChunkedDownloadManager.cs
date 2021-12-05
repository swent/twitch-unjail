using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TwitchUnjail.Core.Models;
using Timer = System.Timers.Timer;

namespace TwitchUnjail.Core.Managers {
    
    public class ChunkedDownloadManager {

        private const int ThreadCount = 6;
        private const int RetryCount = 5;
        private const int SpeedAveragingSeconds = 20;
        
        public delegate void DownloadProgressUpdateHandler(object sender, DownloadProgressUpdateEventArgs eventArgs);
        public event DownloadProgressUpdateHandler? DownloadProgressUpdate;
        
        public ConcurrentQueue<Chunk> Chunks { get; }
        public string TargetFilePath { get; }
        
        private bool _running;
        private DownloadProgressTracker? _progressTracker;
        private DateTime _startTime;
        private Task? _finishedTask;
        private Exception? _encounteredException;
        private int _finishedIndex;
        private int _lastIndex;
        private ConcurrentDictionary<int, Chunk>? _doneQueue;
        private FileStream? _stream;
        private BinaryWriter? _writer;
        private Timer _progressTimer;
        private ConcurrentDictionary<DateTime, long> _bytesDownloadedTracker;
        private ConcurrentDictionary<DateTime, long> _bytesWrittenTracker;
        private Thread[]? _threads;

        public ChunkedDownloadManager(IOrderedEnumerable<string> urlsToDownload, string targetFilePath) {
            var counter = 0;
            Chunks = new ConcurrentQueue<Chunk>(urlsToDownload
                .Select(url => new Chunk { Index = counter++, Url = url }));
            TargetFilePath = targetFilePath;
            _running = false;
            _progressTimer = new Timer();
            _progressTimer.Interval = 300.0;
            _progressTimer.AutoReset = true;
            _progressTimer.Elapsed += OnProgressTimerElapsed;
            _bytesDownloadedTracker = new ConcurrentDictionary<DateTime, long>();
            _bytesWrittenTracker = new ConcurrentDictionary<DateTime, long>();
        }

        public async ValueTask Start(int? threadCount, DownloadProgressTracker? progressTracker = null) {
            if (_running) {
                throw new Exception("Download manager already running.");
            }
            /* Cleanup and preset before run */
            _running = true;
            _progressTracker = progressTracker;
            _startTime = DateTime.Now;
            _finishedTask = new Task(() => {});
            _encounteredException = null;
            _finishedIndex = -1;
            _lastIndex = Chunks.Count - 1;
            _doneQueue = new ConcurrentDictionary<int, Chunk>();
            Directory.CreateDirectory(Path.GetDirectoryName(TargetFilePath)!);
            _stream = new FileStream(TargetFilePath, FileMode.Create);
            _writer = new BinaryWriter(_stream, Encoding.UTF8);

            /* Start threads and do work */
            _threads = Enumerable.Range(0, threadCount ?? ThreadCount)
                .Select(i => new Thread(DoWork))
                .ToArray();
            foreach (var thread in _threads) {
                thread.Start();
            }
            if (progressTracker != null)
                _progressTimer.Start();

            /* Cleanup after run */
            await _finishedTask;
            if (progressTracker != null)
                _progressTimer.Stop();
            await _writer.DisposeAsync();
            await _stream.DisposeAsync();
            _running = false;

            /* Check for errors */
            if (_encounteredException != null) {
                throw _encounteredException;
            }
        }

        private async void DoWork() {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.36 Safari/537.36");

            /* Process until chunks-queue is empty */
            while (_encounteredException == null && Chunks.TryDequeue(out var chunk)) {
                var retryCounter = 0;
                while (!chunk.Done) {
                    try {
                        var response = await httpClient.GetAsync(chunk.Url);
                        response.EnsureSuccessStatusCode();
                        chunk.Content = await response.Content.ReadAsByteArrayAsync();
                        _bytesDownloadedTracker[DateTime.Now] = chunk.Content.Length;
                        MarkFinished(chunk);
                    } catch (Exception ex) {
                        retryCounter++;
                        if (retryCounter > RetryCount) {
                            httpClient.Dispose();
                            AbortThreads(ex);
                            return;
                        } else {
                            /* Wait a little before retrying the same chunk that just failed */
                            Thread.Sleep(10 * 1000);
                        }
                    }
                }
            }
            
            httpClient.Dispose();
        }

        private void MarkFinished(Chunk chunk) {
            _doneQueue![chunk.Index] = chunk;
            chunk.Done = true;
            
            /* Check if this thread passed in the next chunk we are waiting for, if so go into write logic */
            if (chunk.Index == _finishedIndex + 1) {
                var counter = _finishedIndex + 1;
                while (_doneQueue.TryGetValue(counter, out var writeChunk)) {
                    _finishedIndex = counter - 1;
                    _writer!.Write(writeChunk.Content!);
                    _bytesWrittenTracker[DateTime.Now] = writeChunk.Content!.Length;
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

        public void AbortThreads(Exception exception) {
            /* Set exception property and wait for all threads to exit */
            _encounteredException = exception;
            while (_threads.Count(t => t.IsAlive) > 1) {
                Thread.Sleep(1000);
            }
            
            /* Signal main method to continue */
            _finishedTask?.RunSynchronously();
        }
        
        private void OnProgressTimerElapsed(object? sender, ElapsedEventArgs e) {
            var eventArgs = GenerateProgressUpdateEventArgs();
            DownloadProgressUpdate?.Invoke(this, eventArgs);
            if (_progressTracker != null) {
                _progressTracker.SignalProgressUpdate(eventArgs);
            }
        }

        private DownloadProgressUpdateEventArgs GenerateProgressUpdateEventArgs() {
            var total = _lastIndex + 1;
            var written = _finishedIndex + 1;
            var downloaded = written + _doneQueue!.Count;

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

    public class Chunk {
        public int Index { get; set; }
        public string? Url { get; set; }
        public byte[]? Content { get; set; }
        public bool Done { get; set; }
    }

    public class DownloadProgressUpdateEventArgs {
        public string TargetFile { get; set; }
        public double SecondsElapsed { get; set; }
        public int ChunksTotal { get; set; }
        public int ChunksDownloaded { get; set; }
        public int ChunksWritten { get; set; }
        public int DownloadSpeedKBps { get; set; }
        public int WriteSpeedKBps { get; set; }
    }
    
    public class DownloadCompletedStatistics {
        public double SecondsElapsed { get; set; }
        public int ChunksTotal { get; set; }
        public int DownloadSpeedKBps { get; set; }
        public int WriteSpeedKBps { get; set; }
    }
}
