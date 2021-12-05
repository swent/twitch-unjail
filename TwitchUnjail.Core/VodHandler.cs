using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;
using TwitchUnjail.Core.Models.HttpResponses;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Core {

    public static class VodHandler {

        public static async ValueTask<Vod> RetrieveVodInformation(string url) {
            var videoId = Api.GetVideoIdForUrl(url);
            var videoInfoTask = Api.GetVideoInfo(url);
            var tokenResponseTask = Api.GetPlaybackAccessToken(videoId.ToString(), false);

            var videoInfo = await videoInfoTask;
            var tokenResponse = await tokenResponseTask;
            
            /* Validate videoinfo response */
            if (videoInfo == null) {
                throw new Exception("Could not retrieve video info. Try again later.");
            }
            
            /* Validate token response */
            if (tokenResponse == null) {
                throw new Exception("Could not aquire playback token. Try again later.");
            }

            /* Prepare domain checks for the vod */
            var urlMiddlePart = videoInfo.SeekPreviewsUrl
                .Split('/')
                .Skip(3)
                .First();
            var domains = await HttpHelper.GetTwitchDomains();
            var domainCheckTasks = domains
                .Select(domain => HttpHelper.IsUrlReachable($"{domain}/{urlMiddlePart}/chunked/index-dvr.m3u8"))
                .ToArray();

            /* Wait for all url checks to finish */
            await Task.WhenAll(domainCheckTasks);
            string? baseUrl = null;
            for (var i = 0; i < domainCheckTasks.Length; i++) {
                if (domainCheckTasks[i].Result) {
                    baseUrl = domains[i] + "/" + urlMiddlePart;
                    break;
                }
            }

            /* Validate baseUrl found */
            if (string.IsNullOrEmpty(baseUrl)) {
                throw new Exception("No reachable domain for vod found. Try again later.");
            }

            /* Obtain a list of feeds / qualities */
            Dictionary<FeedQuality, string> feeds;
            if (tokenResponse.Data.VideoPlaybackAccessToken.ParsedValue.Chansub.RestrictedBitrates.Length > 0) {
                /* Filter restricted feeds to known quality settings */
                var feedQualities = tokenResponse.Data.VideoPlaybackAccessToken.ParsedValue.Chansub.RestrictedBitrates.Select(key => {
                    try {
                        return (key, EnumHelper.FromKey(key));
                    } catch (Exception) {
                        return (null, FeedQuality.Q160p30)!;
                    }
                });
                feeds = feedQualities
                    .Where(fq => !string.IsNullOrEmpty(fq.key))
                    .ToDictionary(key => key.Item2, value => $"{baseUrl}/{value.key}/index-dvr.m3u8");
            } else {
                /* Download playlist and extract feeds from it */
                var playlist = await HttpHelper.GetHttp(
                    $"https://usher.ttvnw.net/vod/{videoId}.m3u8?sig={tokenResponse.Data.VideoPlaybackAccessToken.Signature}&token={tokenResponse.Data.VideoPlaybackAccessToken.Value.Replace("\\", "")}&allow_source=true&player=twitchweb&allow_spectre=true&allow_audio_only=true");
                var lines = playlist
                    .Split('\n')
                    .Select(s => s.Trim())
                    .ToArray();
                feeds = new Dictionary<FeedQuality, string>();
                for (var i = 0; i < lines.Length; i++) {
                    if (!lines[i].StartsWith("#EXT-X-MEDIA")) continue;
                    var qualityString = lines[i].Split(',').Skip(1).First().Split('"').Skip(1).First();
                    feeds[EnumHelper.FromKey(qualityString)] = lines[i + 2];
                }
            }

            return new Vod(
                videoId,
                videoInfo.Title,
                videoInfo.Channel.Name,
                videoInfo.Channel.DisplayName,
                videoInfo.RecordDate,
                videoInfo.Length,
                feeds);
        }

        public static async ValueTask DownloadVod(Vod vod, FeedQuality quality, string targetPath, string? targetFilename = null, DownloadProgressTracker? progressTracker = null) {
            /* Pick url and load the m3u8 file */
            var qualityFeedUrl = vod.Feeds[quality];
            var baseUrl = string.Join("/", qualityFeedUrl
                .Split('/')
                .SkipLast(1));
            var m3U8 = await HttpHelper.GetHttp(qualityFeedUrl);

            /* Map m3u8 entries to absolute download url for each chunk */
            var chunks = m3U8
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] != '#')
                .Select(line => $"{baseUrl}/{line.Replace("unmuted.ts", "muted.ts")}")
                .OrderBy(line => int.Parse(line.Split('/').Last().Replace(".ts", "").Split('-').First()));

            /* Create the download manager and start download */
            var manager = new ChunkedDownloadManager(
                chunks,
                Path.Combine(
                    FileSystemHelper.EnsurePathWithoutTrailingDelimiter(targetPath),
                    targetFilename ?? GenerateVodFilename(vod)));
            await manager.Start(PickDownloaderThreadCountByQuality(quality), progressTracker);
        }

        private static string GenerateVodFilename(Vod vod) {
            var secondOfDayIdentifier =
                vod.RecordDate.Hour * 3600 + vod.RecordDate.Minute * 60 + vod.RecordDate.Second;
            return FileSystemHelper.StripInvalidChars(
                $"{vod.RecordDate.ToString("dd-MM-yyyy")} - {vod.ChannelDisplayName} - {vod.Title} ({secondOfDayIdentifier}).mp4");
        }

        private static int PickDownloaderThreadCountByQuality(FeedQuality quality) {
            switch (quality) {
                case FeedQuality.Q4Kp60:
                case FeedQuality.Q4Kp30:
                case FeedQuality.Q1440p60:
                case FeedQuality.Q1440p30:
                    return 6;
                case FeedQuality.Q1080p60:
                case FeedQuality.Q1080p30:
                case FeedQuality.Source:
                    return 8;
                case FeedQuality.Q720p60:
                case FeedQuality.Q720p30:
                    return 10;
                case FeedQuality.Q480p60:
                case FeedQuality.Q480p30:
                    return 12;
                case FeedQuality.Q360p60:
                case FeedQuality.Q360p30:
                    return 14;
                default:
                    return 20;
            }
        }
    }
}
