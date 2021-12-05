// See https://aka.ms/new-console-template for more information

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Cli {

    class Cli {
        
        const string vid = "https://www.twitch.tv/videos/1221930435";
        private const int ProgressBarSegments = 40;
        
        static async Task<int> Main(string[] args) {
            
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

                    if (string.IsNullOrEmpty(outPath)) {
                        throw new Exception("No output path has been set, use the '--output' or '-o' arguments");
                    }

                    /* Retrieve vod */
                    var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl);

                    /* Check if requested quality is avilable */
                    var enumQuality = ArgumentsHelper.QualityFromString(quality);
                    if (!vodInfo.Feeds.ContainsKey(enumQuality)) {
                        throw new Exception($"The selected quality '{ArgumentsHelper.QualityEnumToDisplayText(enumQuality)}' is not available.{Environment.NewLine}Available qualities: {string.Join(", ", vodInfo.Feeds.Keys.Select(key => ArgumentsHelper.QualityEnumToDisplayText(key)))}");
                    }

                    /* Start download */
                    var progressTracker = new DownloadProgressTracker(OnDownloadProgressUpdate);
                    await VodHandler.DownloadVod(vodInfo, enumQuality, outPath, outFile, progressTracker);
                    
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
            
            /* Retrieve vod */
            var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl!);
            
            /* Select quality */
            Console.WriteLine(string.Empty);
            Console.WriteLine($"Available qualities: {string.Join(", ", vodInfo.Feeds.Keys.Select(key => ArgumentsHelper.QualityEnumToDisplayText(key)))}");
            FeedQuality? enumQuality = null;
            do {
                Console.Write("Enter the quality to download: ");
                Console.WriteLine(string.Empty);
                var quality = Console.ReadLine();
                try {
                    enumQuality = ArgumentsHelper.QualityFromString(quality);
                    if (!vodInfo.Feeds.ContainsKey((FeedQuality)enumQuality)) {
                        throw new Exception("invalid");
                    }
                } catch (Exception ex) {
                    enumQuality = null;
                    Console.WriteLine("Invalid quality entered.");
                }
            } while (enumQuality == null);

            Console.WriteLine(string.Empty);
            Console.Write("Enter the download path: ");
            var outPath = Console.ReadLine();
            
            /* Start download */
            var progressTracker = new DownloadProgressTracker(OnDownloadProgressUpdate);
            await VodHandler.DownloadVod(vodInfo, (FeedQuality)enumQuality, outPath!, null, progressTracker);
                    
            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }

        private static bool _progressInitialized = false;
        private static int _lastSegmentsDone = -1;
        static void OnDownloadProgressUpdate(DownloadProgressUpdateEventArgs eventArgs) {
            var factorDone = eventArgs.ChunksWritten / (double)eventArgs.ChunksTotal;
            var segmentsDone = (int)Math.Round(ProgressBarSegments * factorDone);
            var segmentString = Enumerable.Range(0, segmentsDone).Aggregate("", (res, cur) => res += "#");

            var percentDone = (factorDone * 100).ToString("F1", CultureInfo.InvariantCulture);
            
            if (!_progressInitialized) {
                Console.Clear();
                Console.WriteLine($"Download to:     {eventArgs.TargetFile}");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"Downloaded     | {eventArgs.ChunksDownloaded} / {eventArgs.ChunksTotal}          ");
                Console.WriteLine($"Written        | {eventArgs.ChunksWritten} / {eventArgs.ChunksTotal}          ");
                Console.WriteLine($"Download speed | {(eventArgs.DownloadSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.WriteLine($"Write speed    | {(eventArgs.WriteSpeedKBps / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} mb/s    ");
                Console.WriteLine(string.Empty);
                Console.WriteLine($"[{segmentString.PadRight(ProgressBarSegments)}] {percentDone.PadLeft(5)}%  ");
                Console.WriteLine(string.Empty);
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
    }
}
