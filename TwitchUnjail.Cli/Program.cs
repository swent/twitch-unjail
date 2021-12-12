using System;
using System.Collections.Generic;
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

        /**
         * Main entry point for the console / cli app.
         * Given arguments device on interactive mode vs cli mode.
         */
        private static async Task<int> Main(string[] args) {
            try {
                var url = ArgumentsHelper.SearchArgument<string>(args, "url");
                var file = ArgumentsHelper.SearchArgument<string>(args, "file");
                var helpArgument = ArgumentsHelper.SearchArgument<bool?>(args, "help")
                    ?? ArgumentsHelper.SearchArgument<bool?>(args, "h");

                /* Run interactive if no vod argument given */
                if (helpArgument == true) {
                    PrintCliHelp();
                } else if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(file)) {
                    await RunInteractive();
                }
                else {
                    /* Run cli mode based on given url */
                    if (!string.IsNullOrEmpty(url)) {
                        await RunVodDownloadCli(url, args);
                    } else {
                        await RunBatchDownloadCli(file, args);
                    }
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
