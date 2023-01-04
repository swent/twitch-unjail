using System.Reflection;
using System.Text.Json;
using TwitchUnjail.Cli.Utilities;
using TwitchUnjail.Core;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Cli {
    
    /**
     * Main class for the console app.
     */
    internal static partial class Program {

        /**
         * Runs the vod download in cli mode, reading all input from process arguments.
         * Will fail if arguments are missing or wrong.
         */
        private static async ValueTask RunVodDownloadCli(string videoUrl, string[] args) {
            /* Read cli arguments */
            var quality = ArgumentsHelper.SearchArgument<string>(args, "quality")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "q")
                          ?? "source";
            var outPath = ArgumentsHelper.SearchArgument<string>(args, "output")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "o")
                          ?? Directory.GetCurrentDirectory();

            var outFile = ArgumentsHelper.SearchArgument<string>(args, "name")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "n");
            
            var useLogging = ArgumentsHelper.SearchArgument<bool?>(args, "log")
                          ?? ArgumentsHelper.SearchArgument<bool?>(args, "l");
            
            /* mbps usually refers to mbit per second which is not what we're using it as here, find a better argument name? */
            var mbytePerSecond = ArgumentsHelper.SearchArgument<double?>(args, "mbps");

            /* Check if direct vod download or recovery via twitchtracker.com */
            Dictionary<FeedQuality, string> availableQualities;
            if (videoUrl.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase)) {
                /* Retrieve vod info */
                var vodInfo = await VodHandler.RetrieveVodInformation(videoUrl);
                availableQualities = vodInfo.Feeds;
                if (string.IsNullOrEmpty(outFile)) {
                    outFile = GenerateDefaultVodFilename(vodInfo);
                }
            } else {
                VodRecoveryInfo recoveryInfo;
                if (videoUrl.Contains("twitchtracker.com", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(videoUrl);
                } else if (videoUrl.Contains("streamscharts.com", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await StreamsChartsHandler.RetrieveInfo(videoUrl);
                } else {
                    throw new Exception($"The given url is not a known vod url: '{videoUrl}'");
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
            var proxy = new DownloadManagerProxy(OnDownloadProgressUpdate);
            var targetFilePath = Path.Combine(
                FileSystemHelper.EnsurePathWithoutTrailingDelimiter(outPath),
                outFile);
            FileLogManager logManager = null;
            if (useLogging == true) {
                var targetFileParts = targetFilePath.Split('.');
                logManager = new FileLogManager(string.Join(".", targetFileParts.Take(targetFileParts.Length - 1)) + ".log");
            }
            InitializeDownloadProgressView(targetFilePath);
            proxy.StartReadingKeyCommands();
            await VodHandler.DownloadMp4(availableQualities[enumQuality], enumQuality, targetFilePath, mbytePerSecond != null ? (int?)(mbytePerSecond * 1024) : null, proxy, logManager);
            proxy.StopReadingKeyCommands();
            
            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }
        
        /**
         * Reads a given file from disk and uses content either as json-url-array or
         * simple multiline-url-file to batch-download multiple urls sequentially.
         */
        internal static async ValueTask RunBatchDownloadCli(string filePath, string[] args) {
            if (File.Exists(filePath)) {
                /* Read file and try as json or just regular multi-line file */
                var content = File.ReadAllText(filePath);
                string[] urls = null;
                try {
                    urls = JsonSerializer.Deserialize<string[]>(content);
                } catch { /* ignored */ }
                if (urls == null) {
                    urls = content
                        .Split('\n')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                }
                
                /* Download each url */
                var downloadResults = new List<(string, Exception)>();
                foreach (var url in urls) {
                    try {
                        await RunVodDownloadCli(url, args);
                        downloadResults.Add((url, null));
                    }
                    catch (Exception e) {
                        downloadResults.Add((url, e));
                    }
                }
                
                /* Print results */
                Console.WriteLine(string.Empty);
                Console.WriteLine($"All {downloadResults.Count} downloads completed.");
                Console.WriteLine("Results:");
                foreach (var result in downloadResults) {
                    var shortUrl = result.Item1.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        ? result.Item1.Substring(8)
                        : result.Item1;
                    Console.WriteLine($"  {shortUrl}: {(result.Item2 == null ? "success" : $"failed: {result.Item2.Message}")}");
                }
            }
        }

        /**
         * Runs the m3u8 download in cli mode, reading all input from process arguments.
         * Will fail if arguments are missing or wrong.
         */
        internal static async ValueTask RunM3U8DownloadCli(string videoUrl, string[] args) {
            /* Read cli arguments */
            var quality = ArgumentsHelper.SearchArgument<string>(args, "quality")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "q")
                          ?? "source";
            
            var outPath = ArgumentsHelper.SearchArgument<string>(args, "output")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "o")
                          ?? Directory.GetCurrentDirectory();

            var outFile = ArgumentsHelper.SearchArgument<string>(args, "name")
                          ?? ArgumentsHelper.SearchArgument<string>(args, "n");

            /* Check if direct vod download or recovery via twitchtracker.com */
            Dictionary<FeedQuality, string> availableQualities;
            if (videoUrl.StartsWith("https://www.twitch.tv/")) {
                /* Retrieve vod info */
                var vodInfo = await VodHandler.RetrieveVodInformation(videoUrl);
                availableQualities = vodInfo.Feeds;
                if (string.IsNullOrEmpty(outFile)) {
                    outFile = GenerateDefaultVodFilename(vodInfo);
                }
            } else {
                VodRecoveryInfo recoveryInfo;
                if (videoUrl.StartsWith("https://twitchtracker.com/")) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await TwitchTrackerHandler.RetrieveInfo(videoUrl);
                } else if (videoUrl.StartsWith("https://streamscharts.com/")) {
                    Console.WriteLine("Switching to recovery mode.");
                    /* Retrieve recovery info */
                    recoveryInfo = await StreamsChartsHandler.RetrieveInfo(videoUrl);
                } else {
                    throw new Exception($"The given url is not a known vod url: '{videoUrl}'");
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

            /* Download file */
            var targetFilePath = Path.Combine(
                FileSystemHelper.EnsurePathWithoutTrailingDelimiter(outPath),
                outFile);
            await VodHandler.DownloadM3U8(availableQualities[enumQuality], targetFilePath);

            /* Write success */
            Console.WriteLine(string.Empty);
            Console.WriteLine("Download completed successfully.");
        }

        /**
         * Prints a help screen containing all available cli arguments.
         */
        internal static void PrintCliHelp() {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            
            Console.WriteLine($"twitch-unjail {(version == null ? string.Empty : "v" + version.ToString(2))}");
            Console.WriteLine(string.Empty);
            Console.WriteLine("All cli arguments:");
            Console.WriteLine("  --url URL               Url to download");
            Console.WriteLine("  --file PATH             Path of file containing urls to download");
            Console.WriteLine("  --quality / -q QUALITY  Quality to download a vod in");
            Console.WriteLine("  --name / -n NAME        Save file name");
            Console.WriteLine("  --mbps SPEED            Fixed speed limit (in mb/s) while downloading");
            Console.WriteLine("  --log / -l              Creates a log file containing detailed processing information");
            Console.WriteLine("  --output / -o PATH      Path where the downloaded file is stored");
        }
    }
}
