using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Cli {
    
    /**
     * Main class for the console app.
     */
    internal static partial class Program {
        
        /**
         * Runs the app interactive mode, querying the user to enter relevant input on stdin.
         */
        private static async ValueTask RunInteractive() {
            Console.Write("Enter the vod url to download: ");
            var vodUrl = Console.ReadLine();
            
            /* Check if direct vod download or recovery */
            string outFile;
            Dictionary<FeedQuality, string> availableQualities;
            if (vodUrl != null && vodUrl.StartsWith("https://www.twitch.tv/")) {
                /* Retrieve vod */
                var vodInfo = await VodHandler.RetrieveVodInformation(vodUrl);
                availableQualities = vodInfo.Feeds;
                outFile = GenerateDefaultVodFilename(vodInfo);
            } else {
                VodRecoveryInfo recoveryInfo;
                if (vodUrl != null && vodUrl.StartsWith("https://twitchtracker.com/")) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(vodUrl);
                } else if (vodUrl != null && vodUrl.StartsWith("https://streamscharts.com/")) {
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
            Console.Write($"Enter the download path: ({Directory.GetCurrentDirectory()})");
            var outPath = Console.ReadLine();
            if (string.IsNullOrEmpty(outPath)) outPath = Directory.GetCurrentDirectory();
            
            /* Start download */
            var progressTracker = new DownloadProgressTracker(OnDownloadProgressUpdate);
            await VodHandler.DownloadVod(availableQualities[(FeedQuality)enumQuality], (FeedQuality)enumQuality, outPath!, outFile, null, progressTracker);
                    
            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }
    }
}
