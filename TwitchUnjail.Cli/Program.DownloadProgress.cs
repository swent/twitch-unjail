using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TwitchUnjail.Core.Managers;

namespace TwitchUnjail.Cli {
    
    internal static partial class Program {
        
        private const int ProgressBarSegments = 40;
        private const int TimeAveragingSegments = 60;
        
        // /* Used to track number of progress bar segments in update screen */
        // private static int _lastSegmentsDone = -1;
        /* Factor done averaging queue */
        private static Queue<double> _factorDoneValues = new();
        /* Time remaining averaging queue */
        private static Queue<double> _timeRemainingValues = new();
        
        
        /**
         * Resets all progress view metrics and triggers a clean draw of the progress view.
         */
        private static void InitializeDownloadProgressView(string targetFilename) {
            // _lastSegmentsDone = -1;
            lock (_factorDoneValues) {
                _factorDoneValues.Clear();
            }
            lock (_timeRemainingValues) {
                _timeRemainingValues.Clear();
            }

            Console.Clear();
            PrintProgressUpdateView(targetFilename, 0, 0, 0, 0.0, 0.0, 0.0, "-", false, null);
        }

        /**
         * Called when the {DownloadProgressTracker} sends an update to refresh
         * download information on screen.
         */
        private static void OnDownloadProgressUpdate(DownloadProgressUpdateEventArgs eventArgs) {
            var factorDone = (eventArgs.ChunksDownloaded / (double)eventArgs.ChunksTotal + eventArgs.ChunksWritten / (double)eventArgs.ChunksTotal) / 2;

            /* Calculate time remaining */
            /* Uses a queue to store the recently calculated factorDone values over a fixed amount of time
               and then divides the specified timeframe by the  average factorDone over that timeframe to get
               an average seconds remaining using the "speed" from the recent timeframe */
            string etaString;
            if (eventArgs.IsPaused) {
                etaString = "PAUSED";
            } else {
                double secondsRemaining;
                lock (_factorDoneValues) {
                    secondsRemaining = _factorDoneValues.Count * ChunkedDownloadManager.ProgressUpdateIntervalMilliseconds / 1000.0 / Math.Max(factorDone - (_factorDoneValues.Count > 0 ? _factorDoneValues.Peek() : 0.0), 0.005) * (1.0 - factorDone);
                    _factorDoneValues.Enqueue(factorDone);
                    if (_factorDoneValues.Count > TimeAveragingSegments) _factorDoneValues.Dequeue();
                }
                double averageSecondsRemaining;
                lock (_timeRemainingValues) {
                    _timeRemainingValues.Enqueue(secondsRemaining);
                    if (_timeRemainingValues.Count > TimeAveragingSegments / 2) _timeRemainingValues.Dequeue();
                    averageSecondsRemaining = _timeRemainingValues.Sum() / _timeRemainingValues.Count;
                }
                var timeRemaining = TimeSpan.FromSeconds(Math.Max(averageSecondsRemaining - TimeAveragingSegments / 2 * (ChunkedDownloadManager.ProgressUpdateIntervalMilliseconds / 1000.0), 0.0));
                etaString = "ETA: " + timeRemaining.ToString(timeRemaining .TotalMinutes > 59 ? "hh\\:mm\\:ss" : "mm\\:ss").PadRight(8);
            }

            PrintProgressUpdateView(eventArgs.TargetFile, eventArgs.ChunksDownloaded, eventArgs.ChunksWritten, eventArgs.ChunksTotal, eventArgs.DownloadSpeedKBps, eventArgs.WriteSpeedKBps, factorDone, etaString, eventArgs.IsPaused, eventArgs.TargetSpeedLimitKbPerSecond);
        }

        private static void PrintProgressUpdateView(string targetFilename, int downloaded, int written, int total, double downloadSpeedKBps, double writeSpeedKBps, double factorDone, string eta, bool isPaused, int? limitKbPerSecond) {
            var consoleWidth = Console.WindowWidth;
            var percentDone = (factorDone * 100).ToString("F1", CultureInfo.InvariantCulture);
            var segmentsDone = (int)Math.Round(ProgressBarSegments * factorDone);
            var segmentString = Enumerable.Range(0, segmentsDone).Aggregate("", (res, _) => res + "#");

            Console.SetCursorPosition(0, 0);
            Console.Write("Download to: ");
            Console.SetCursorPosition(0, 1);
            Console.Write(string.Empty.PadRight(consoleWidth));
            Console.SetCursorPosition(0, 2);
            Console.Write($"Downloaded     | {downloaded} / {total}".PadRight(consoleWidth));
            Console.SetCursorPosition(0, 3);
            Console.Write($"Written        | {written} / {total}".PadRight(consoleWidth));
            Console.SetCursorPosition(0, 4);
            Console.Write($"Download speed | {(downloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s {(limitKbPerSecond == null ? "" : $"(limited to {((int)limitKbPerSecond / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s)")}".PadRight(consoleWidth));
            Console.SetCursorPosition(0, 5);
            Console.Write($"Write speed    | {(writeSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s".PadRight(consoleWidth));
            Console.SetCursorPosition(0, 6);
            Console.Write(string.Empty.PadRight(consoleWidth));
            Console.SetCursorPosition(0, 7);
            Console.Write($"[{segmentString.PadRight(ProgressBarSegments)}] {percentDone.PadLeft(5)}%  {eta}".PadRight(consoleWidth));
            Console.SetCursorPosition(0, 8);
            Console.Write(string.Empty.PadRight(consoleWidth));
            
            /* Write filename last since it might break line */
            Console.SetCursorPosition(13, 0);
            Console.Write(targetFilename);
            Console.SetCursorPosition(0, 8);
            
            /* Shortcut info */
            if (Console.WindowWidth > 12) {
                Console.SetCursorPosition(0, 9);
                Console.Write(string.Empty.PadRight(consoleWidth));
                Console.SetCursorPosition(0, 10);
                Console.Write(" [ P ] pause        [ F ] fixed speed limit".PadRight(consoleWidth));
                Console.SetCursorPosition(0, 11);
                Console.Write(" [ + ] speed limit  [ - ] speed limit".PadRight(consoleWidth));
                Console.SetCursorPosition(0, 12);
                Console.Write(string.Empty.PadRight(consoleWidth));
                Console.SetCursorPosition(0, 12);
            }
        }
    }
}
