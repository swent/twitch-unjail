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
         * Runs the app in cli mode, reading all input from process arguments.
         * Will fail if arguments are missing or wrong.
         */
        internal static async ValueTask RunCli(string vodUrl, string[] args) {
            /* Read cli arguments */
            var quality = ArgumentsHelper.SearchArgument<string>(args, "quality")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "q")
                          ?? "source";
            var outPath = ArgumentsHelper.SearchArgument<string>(args, "output")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "o")
                          ?? Directory.GetCurrentDirectory();

            var outFile = ArgumentsHelper.SearchArgument<string>(args, "name")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "n");
            
            /* mbps usually refers to mbit per second which is not what we're using it as here, find a better argument name? */
            var mbytePerSecond = ArgumentsHelper.SearchArgument<double?>(args, "mbps");

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
    }
}
