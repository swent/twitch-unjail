// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Cli {

    class Cli {
        
        const string vid = "https://www.twitch.tv/videos/1221930435";
        private const int ProgressBarSegments = 40;
        
        static async Task<int> Main(string[] args) {

            // var a = await VodHandler.RecoverVodFeeds("https://twitchtracker.com/minpojke/streams/43953806636");
            // return 0;
            
            try {
                var vodUrl = ArgumentsHelper.SearchArgument<string>(args, "vod");

                if (string.IsNullOrEmpty(vodUrl)) {
                    await InputFromStdin();
                }
                else {
                    /* Read cli arguments */
                    var quality = ArgumentsHelper.SearchArgument<string>(args, "quality")
                                  ?? ArgumentsHelper.SearchArgument<string>(args, "q")
                                  ?? "source";
                    var outPath = ArgumentsHelper.SearchArgument<string>(args, "output")
                                  ?? ArgumentsHelper.SearchArgument<string>(args, "o");

                    var outFile = ArgumentsHelper.SearchArgument<string>(args, "name")
                                  ?? ArgumentsHelper.SearchArgument<string>(args, "n");
                    
                    var mbytePerSecond = ArgumentsHelper.SearchArgument<double?>(args, "mbps");

                    if (string.IsNullOrEmpty(outPath)) {
                        throw new Exception("No output path has been set, use the '--output' or '-o' arguments");
                    }

                    /* Check if direct vod download or recovery via twitchtracker.com */
                    Dictionary<FeedQuality, string> availableQualities;
                    if (vodUrl.StartsWith("https://www.twitch.tv/")) {
                        /* Retrieve vod */
                        var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl);
                        availableQualities = vodInfo.Feeds;
                        if (string.IsNullOrEmpty(outFile)) {
                            outFile = GenerateDefaultVodFilename(vodInfo);
                        }
                    } else {
                        VodRecoveryInfo recoveryInfo;
                        if (vodUrl.StartsWith("https://twitchtracker.com/")) {
                            Console.WriteLine("Switching to recovery mode.");
                            /* Retrieve recovery info */
                            recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(vodUrl);
                        } else if (vodUrl.StartsWith("https://streamscharts.com/")) {
                            Console.WriteLine("Switching to recovery mode.");
                            /* Retrieve recovery info */
                            recoveryInfo = await StreamsChartsHandler.RetrieveInfo(vodUrl);
                        } else {
                            throw new Exception($"The given url is not a known vod url: '{vodUrl}'");
                        }
                        /* Try to recover feeds */
                        availableQualities = await VodHandler.RecoverVodFeeds(recoveryInfo);
                        if (availableQualities == null) {
                            throw new Exception("Vod could not be recovered, not found on twitch servers.");
                        }
                        if (string.IsNullOrEmpty(outFile)) {
                            outFile = GenerateRecoveredVodFilename(recoveryInfo);
                        }
                    }
                    
                    /* Check if requested quality is avilable */
                    var enumQuality = ArgumentsHelper.QualityFromString(quality);
                    if (!availableQualities.ContainsKey(enumQuality)) {
                        throw new Exception($"The selected quality '{ArgumentsHelper.QualityEnumToDisplayText(enumQuality)}' is not available.{Environment.NewLine}Available qualities: {string.Join(", ", availableQualities.Keys.Select(key => ArgumentsHelper.QualityEnumToDisplayText(key)))}");
                    }

                    /* Start download */
                    var progressTracker = new DownloadProgressTracker(OnDownloadProgressUpdate);
                    await VodHandler.DownloadVod(availableQualities[enumQuality], enumQuality, outPath, outFile, mbytePerSecond != null ? (int?)(mbytePerSecond * 1024) : null, progressTracker);
                    
                    /* Write success */
                    Console.WriteLine(string.Empty);
                    Console.WriteLine("Download completed successfully.");
                }
                return 0;
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
        }

        private static async Task InputFromStdin() {
            Console.Write("Enter the vod url to download: ");
            var vodUrl = Console.ReadLine();
            
            /* Check if direct vod download or recovery via twitchtracker.com */
            string outFile;
            Dictionary<FeedQuality, string> availableQualities;
            if (vodUrl.StartsWith("https://www.twitch.tv/")) {
                /* Retrieve vod */
                var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl);
                availableQualities = vodInfo.Feeds;
                outFile = GenerateDefaultVodFilename(vodInfo);
            } else {
                VodRecoveryInfo recoveryInfo;
                if (vodUrl.StartsWith("https://twitchtracker.com/")) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(vodUrl);
                } else if (vodUrl.StartsWith("https://streamscharts.com/")) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await StreamsChartsHandler.RetrieveInfo(vodUrl);
                } else {
                    throw new Exception($"The given url is not a known vod url: '{vodUrl}'");
                }
                /* Try to recover feeds */
                availableQualities = await VodHandler.RecoverVodFeeds(recoveryInfo);
                if (availableQualities == null) {
                    throw new Exception("Vod could not be recovered, not found on twitch servers.");
                }
                outFile = GenerateRecoveredVodFilename(recoveryInfo);
            }

            /* Select quality */
            Console.WriteLine(string.Empty);
            Console.WriteLine($"Available qualities: {string.Join(", ", availableQualities.Keys.Select(key => ArgumentsHelper.QualityEnumToDisplayText(key)))}");
            FeedQuality? enumQuality;
            do {
                Console.Write("Enter the quality to download: ");
                Console.WriteLine(string.Empty);
                var quality = Console.ReadLine();
                try {
                    enumQuality = ArgumentsHelper.QualityFromString(quality);
                    if (!availableQualities.ContainsKey((FeedQuality)enumQuality)) {
                        throw new Exception("invalid");
                    }
                } catch (Exception) {
                    enumQuality = null;
                    Console.WriteLine("Invalid quality entered.");
                }
            } while (enumQuality == null);

            Console.WriteLine(string.Empty);
            Console.Write("Enter the download path: ");
            var outPath = Console.ReadLine();
            
            /* Start download */
            var progressTracker = new DownloadProgressTracker(OnDownloadProgressUpdate);
            await VodHandler.DownloadVod(availableQualities[(FeedQuality)enumQuality], (FeedQuality)enumQuality, outPath!, outFile, null, progressTracker);
                    
            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }

        private static bool _progressInitialized;
        private static int _lastSegmentsDone = -1;
        static void OnDownloadProgressUpdate(DownloadProgressUpdateEventArgs eventArgs) {
            var factorDone = eventArgs.ChunksWritten / (double)eventArgs.ChunksTotal;
            var segmentsDone = (int)Math.Round(ProgressBarSegments * factorDone);
            var segmentString = Enumerable.Range(0, segmentsDone).Aggregate("", (res, _) => res += "#");

            var percentDone = (factorDone * 100).ToString("F1", CultureInfo.InvariantCulture);
            
            if (!_progressInitialized) {
                Console.Clear();
                Console.WriteLine("Download to: ");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"Downloaded     | {eventArgs.ChunksDownloaded} / {eventArgs.ChunksTotal}          ");
                Console.WriteLine($"Written        | {eventArgs.ChunksWritten} / {eventArgs.ChunksTotal}          ");
                Console.WriteLine($"Download speed | {(eventArgs.DownloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.WriteLine($"Write speed    | {(eventArgs.WriteSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"[{segmentString.PadRight(ProgressBarSegments)}] {percentDone.PadLeft(5)}%  ");
                Console.WriteLine(string.Empty);
                Console.SetCursorPosition(13, 0);
                Console.WriteLine(eventArgs.TargetFile);
                _progressInitialized = true;
            } else {
                Console.SetCursorPosition(17, 2);
                Console.Write($"{eventArgs.ChunksDownloaded} / {eventArgs.ChunksTotal}          ");
                Console.SetCursorPosition(17, 3);
                Console.Write($"{eventArgs.ChunksWritten} / {eventArgs.ChunksTotal}          ");
                Console.SetCursorPosition(17, 4);
                Console.Write($"{(eventArgs.DownloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.SetCursorPosition(17, 5);
                Console.Write($"{(eventArgs.WriteSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.SetCursorPosition(3 + ProgressBarSegments, 7);
                Console.Write($"{percentDone.PadLeft(5)}%  ");
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
        
        private static string GenerateDefaultVodFilename(Vod vod) {
            var secondOfDayIdentifier =
                vod.RecordDate.Hour * 3600 + vod.RecordDate.Minute * 60 + vod.RecordDate.Second;
            return FileSystemHelper.StripInvalidChars(
                $"{vod.RecordDate.ToString("dd-MM-yyyy")} - {vod.ChannelDisplayName} - {vod.Title} ({secondOfDayIdentifier}).mp4");
        }
        
        private static string GenerateRecoveredVodFilename(VodRecoveryInfo ttInfo) {
            var secondOfDayIdentifier =
                ttInfo.RecordDate.Hour * 3600 + ttInfo.RecordDate.Minute * 60 + ttInfo.RecordDate.Second;
            return FileSystemHelper.StripInvalidChars(
                $"{ttInfo.RecordDate.ToString("dd-MM-yyyy")} - {ttInfo.ChannelDisplayName} ({secondOfDayIdentifier}).mp4");
        }
    }
}
