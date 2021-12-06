using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Cli {
    
    /**
     * Main class for the console app.
     */
    internal static partial class Program {
        
        private const int ProgressBarSegments = 40;

        /**
         * Main entry point for the console / cli app.
         * Given arguments device on interactive mode vs cli mode.
         */
        private static async Task<int> Main(string[] args) {
            try {
                var vodUrl = ArgumentsHelper.SearchArgument<string>(args, "vod");

                /* Run interactive if no vod argument given */
                if (string.IsNullOrEmpty(vodUrl)) {
                    await RunInteractive();
                }
                else {
                    await RunCli(vodUrl, args);
                }
            } catch (Exception ex) {
                /* Print error */
                await Console.Error.WriteLineAsync(string.Empty);
                await Console.Error.WriteLineAsync("ERR");
                await Console.Error.WriteLineAsync(ex.Message
                                                   + Environment.NewLine
                                                   + Environment.NewLine
                                                   + ex.StackTrace);
                return 1;
            }
            
            return 0;
        }
        
        /* Used to track one-time initialization of full progress text */
        private static bool _progressInitialized;
        /* Used to track number of progress bar segments in update screen */
        private static int _lastSegmentsDone = -1;

        /**
         * Called when the {DownloadProgressTracker} sends an update to refresh
         * download information on screen.
         */
        private static void OnDownloadProgressUpdate(DownloadProgressUpdateEventArgs eventArgs) {
            var factorDone = eventArgs.ChunksWritten / (double)eventArgs.ChunksTotal;
            var segmentsDone = (int)Math.Round(ProgressBarSegments * factorDone);
            var segmentString = Enumerable.Range(0, segmentsDone).Aggregate("", (res, _) => res + "#");
            var percentDone = (factorDone * 100).ToString("F1", CultureInfo.InvariantCulture);
            
            if (!_progressInitialized) {
                /* Write full text if not initialized */
                Console.Clear();
                Console.WriteLine("Download to: ");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"Downloaded     | {eventArgs.ChunksDownloaded} / {eventArgs.ChunksTotal}");
                Console.WriteLine($"Written        | {eventArgs.ChunksWritten} / {eventArgs.ChunksTotal}");
                Console.WriteLine($"Download speed | {(eventArgs.DownloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s");
                Console.WriteLine($"Write speed    | {(eventArgs.WriteSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"[{segmentString.PadRight(ProgressBarSegments)}] {percentDone.PadLeft(5)}%");
                Console.WriteLine(string.Empty);
                Console.SetCursorPosition(13, 0);
                Console.WriteLine(eventArgs.TargetFile);
                _progressInitialized = true;
            } else {
                /* Only update partially */
                Console.SetCursorPosition(17, 2);
                Console.Write($"{eventArgs.ChunksDownloaded} / {eventArgs.ChunksTotal}");
                Console.SetCursorPosition(17, 3);
                Console.Write($"{eventArgs.ChunksWritten} / {eventArgs.ChunksTotal}");
                Console.SetCursorPosition(17, 4);
                Console.Write($"{(eventArgs.DownloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s");
                Console.SetCursorPosition(17, 5);
                Console.Write($"{(eventArgs.WriteSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s");
                Console.SetCursorPosition(3 + ProgressBarSegments, 7);
                Console.Write($"{percentDone.PadLeft(5)}%");
            }

            /* Update progress bar if number of segments changed */
            if (_lastSegmentsDone != segmentsDone) {
                Console.SetCursorPosition(0, 7);
                Console.Write($"[{segmentString.PadRight(ProgressBarSegments)}]");
                _lastSegmentsDone = segmentsDone;
            }
            
            /* Set cursor to bottom */
            Console.SetCursorPosition(0, 8);
        }
        
        /**
         * Helper method to generate a vod filename based on vod information.
         */
        private static string GenerateDefaultVodFilename(Vod vod) {
            var secondOfDayIdentifier =
                vod.RecordDate.Hour * 3600 + vod.RecordDate.Minute * 60 + vod.RecordDate.Second;
            return FileSystemHelper.StripInvalidChars(
                $"{vod.RecordDate.ToString("dd-MM-yyyy")} - {vod.ChannelDisplayName} - {vod.Title} ({secondOfDayIdentifier}).mp4");
        }
        
        /**
         * Helper method to generate a vod filename based on recovery information.
         */
        private static string GenerateRecoveredVodFilename(VodRecoveryInfo recoveryInfo) {
            var secondOfDayIdentifier =
                recoveryInfo.RecordDate.Hour * 3600 + recoveryInfo.RecordDate.Minute * 60 + recoveryInfo.RecordDate.Second;
            return FileSystemHelper.StripInvalidChars(
                $"{recoveryInfo.RecordDate.ToString("dd-MM-yyyy")} - {recoveryInfo.ChannelDisplayName} ({secondOfDayIdentifier}).mp4");
        }
    }
}
