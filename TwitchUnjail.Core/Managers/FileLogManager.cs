using System;
using System.Collections.Generic;
using System.Timers;

namespace TwitchUnjail.Core.Managers {
    
    public class FileLogManager {
        
        public string FilePath { get; }

        private object _lock;
        private List<string> _writeQueue;
        private System.Timers.Timer _timer;
        private DateTime _startTime;

        public FileLogManager(string filePath) {
            FilePath = filePath;
            _lock = new object();
            _writeQueue = new List<string>();
            _timer = new System.Timers.Timer();
            _timer.Interval = 1000;
            _timer.AutoReset = true;
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
            _startTime = DateTime.Now;
        }

        ~FileLogManager() {
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
        }

        public void Log(string text) {
            lock (_lock) {
                _writeQueue.Add($"{GetTimestamp()} {text}");
            }
        }

        private string GetTimestamp() {
            var timestamp = (DateTime.Now - _startTime).ToString("hh\\:mm\\:ss\\.fff");
            return timestamp;
        }
        
        private void OnTimerElapsed(object sender, ElapsedEventArgs e) {
            FlushWritingQueue();
        }

        private void FlushWritingQueue() {
            string[] toWrite;
            lock (_lock) {
                toWrite = _writeQueue.ToArray();
                _writeQueue.Clear();
            }
            System.IO.File.AppendAllText(FilePath, $"{string.Join(Environment.NewLine, toWrite)}{Environment.NewLine}");
        }
    }
}
