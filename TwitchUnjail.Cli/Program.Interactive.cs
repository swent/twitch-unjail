using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Cli {
    
    /**
     * Main class for the console app.
     */
    internal static partial class Program {
        
        /**
         * Runs the app interactive mode, querying the user to enter relevant input on stdin.
         */
        private static async ValueTask RunInteractive() {
            /* Read download option */
            // Console.WriteLine("Please choose:");
            // Console.WriteLine("  1. Download to mp4");
            // Console.WriteLine("  2. Download m3u8 file");
            // int? option = null;
            // do {
            //     Console.Write("  ");
            //     var optionString = Console.ReadLine();
            //     if (int.TryParse(optionString?.Replace(".", string.Empty), out var parsed) && parsed > 0 && parsed < 3) {
            //         option = parsed;
            //     } else {
            //         Console.WriteLine("Invalid option entered.");
            //     }
            // } while (option == null);
            
            /* Read url */
            // Console.WriteLine(string.Empty);
            Console.Write("Enter the vod url: ");
            var vodUrl = Console.ReadLine();
            
            /* Check if direct vod download or recovery */
            string outFile;
            Dictionary<FeedQuality, string> availableQualities;
            if (vodUrl != null && vodUrl.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase)) {
                /* Retrieve vod */
                var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl);
                availableQualities = vodInfo.Feeds;
                outFile = GenerateDefaultVodFilename(vodInfo);
            } else {
                VodRecoveryInfo recoveryInfo;
                if (vodUrl != null && vodUrl.Contains("twitchtracker.com", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(vodUrl);
                } else if (vodUrl != null && vodUrl.Contains("streamscharts.com", StringComparison.OrdinalIgnoreCase)) {
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

            /* Quality */
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

            /* Path */
            Console.WriteLine(string.Empty);
            Console.Write($"Enter the download path: ({Directory.GetCurrentDirectory()}) ");
            var outPath = Console.ReadLine();
            if (string.IsNullOrEmpty(outPath)) outPath = Directory.GetCurrentDirectory();

            /* Start download */
            var targetFilePath = Path.Combine(
                FileSystemHelper.EnsurePathWithoutTrailingDelimiter(outPath),
                outFile);
            if (/*option == 1*/ true) {
                var proxy = new DownloadManagerProxy(OnDownloadProgressUpdate);
                InitializeDownloadProgressView(targetFilePath);
                proxy.StartReadingKeyCommands();
                await VodHandler.DownloadMp4(availableQualities[(FeedQuality)enumQuality], (FeedQuality)enumQuality, targetFilePath, null, proxy);
                proxy.StopReadingKeyCommands();
            } else {
                await VodHandler.DownloadM3U8(availableQualities[(FeedQuality)enumQuality], targetFilePath);
            }

            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }
    }
}
