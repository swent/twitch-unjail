using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Core.Managers;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Models.Enums;
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
                throw new Exception("Could not acquire playback token. Try again later.");
            }

            /* Obtain a list of feeds / qualities */
            Dictionary<FeedQuality, string> feeds;
            if (tokenResponse.Data.VideoPlaybackAccessToken.ParsedValue.Chansub.RestrictedBitrates.Length > 0) {
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
                string baseUrl = null;
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
                
                /* Filter restricted feeds to known quality settings */
                var feedQualities = tokenResponse.Data.VideoPlaybackAccessToken.ParsedValue.Chansub.RestrictedBitrates.Select(key => {
                    try {
                        return (key, EnumHelper.FeedQualityFromKey(key));
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
                    feeds[EnumHelper.FeedQualityFromKey(qualityString)] = lines[i + 2];
                }
            }

            return new Vod(
                videoId,
                videoInfo.BroadcastId,
                videoInfo.Title,
                videoInfo.Channel.Name,
                videoInfo.Channel.DisplayName,
                videoInfo.RecordDate,
                videoInfo.Length,
                feeds);
        }

        public static async ValueTask DownloadVod(string feedUrl, FeedQuality quality, string targetPath, string targetFilename, int? targetKbps, DownloadProgressTracker progressTracker = null) {
            /* Pick url and load the m3u8 file */
            var baseUrl = string.Join("/", feedUrl
                .Split('/')
                .SkipLast(1));
            var m3U8 = await HttpHelper.GetHttp(feedUrl);

            /* Map m3u8 entries to absolute download url for each chunk */
            var chunkedParts = M3U8Helper.ExtractLinks(m3U8, baseUrl)
                .Where(part => part.Length > 1) /* Drop parts that consist of a single segment/chunk */
                .ToArray();

            /* Create the download manager and start download */
            var manager = new ChunkedDownloadManager(
                chunkedParts,
                Path.Combine(
                    FileSystemHelper.EnsurePathWithoutTrailingDelimiter(targetPath),
                    targetFilename));
            await manager.Start(PickDownloaderThreadCountByQualityAndTargetSpeed(quality, targetKbps), targetKbps, progressTracker);
        }

        public static async ValueTask<Dictionary<FeedQuality, string>> RecoverVodFeeds(VodRecoveryInfo ttInfo) {
            var domains = await HttpHelper.GetTwitchDomains();

            /* Brute-force +-60 seconds around given record time to find valid vod url */
            string reachableUrl = null;
            var increment = 0;
            while (increment <= 60) {
                var timestamp = (ttInfo.RecordDate + TimeSpan.FromSeconds(increment)).ToUnixTimestamp();
                var baseUrl = GenerateVodBaseUrl(ttInfo.ChannelName, ttInfo.BroadcastId, timestamp);
                var urlsToCheck = domains
                    .Select(domain => $"{domain}/{baseUrl}/chunked/index-dvr.m3u8")
                    .ToArray();
                var tasks = urlsToCheck
                    .Select(HttpHelper.IsUrlReachable)
                    .ToArray();
                await Task.WhenAll(tasks);
                for (var i = 0; i < tasks.Length; i++) {
                    if (tasks[i].Result) {
                        reachableUrl = $"{domains[i]}/{baseUrl}";
                        increment = -10000;
                        break;
                    }
                }
                if (increment > 0) {
                    increment *= -1;
                } else {
                    increment = increment * -1 + 1;
                }
            }

            if (reachableUrl == null) {
                return null;
            } else {
                /* Use all qualities to brute-force which of them are reachable for the found vod-url */
                var qualities = new[] {
                    FeedQuality.Source,
                    FeedQuality.Q4Kp60,
                    FeedQuality.Q4Kp30,
                    FeedQuality.Q1440p60,
                    FeedQuality.Q1440p30,
                    FeedQuality.Q1080p60,
                    FeedQuality.Q1080p30,
                    FeedQuality.Q720p60,
                    FeedQuality.Q720p30,
                    FeedQuality.Q480p60,
                    FeedQuality.Q480p30,
                    FeedQuality.Q360p60,
                    FeedQuality.Q360p30,
                    FeedQuality.Q160p30,
                    FeedQuality.Q144p30,
                    FeedQuality.AudioOnly, }
                    .Select(quality => (quality, quality.ToKey()))
                    .ToArray();
                var urlsToCheck = qualities
                    .Select(quality => $"{reachableUrl}/{quality.Item2}/index-dvr.m3u8")
                    .ToArray();
                var tasks = urlsToCheck
                    .Select(url => HttpHelper.IsUrlReachable(url))
                    .ToArray();
                await Task.WhenAll(tasks);
                var vodFeeds = new Dictionary<FeedQuality, string>();
                for (var i = 0; i < tasks.Length; i++) {
                    if (tasks[i].Result) {
                        vodFeeds[qualities[i].quality] = urlsToCheck[i];
                    }
                }
                return vodFeeds;
            }
        }

        private static string GenerateVodBaseUrl(string channelName, long broadcastId, long timestamp) {
            var baseUrl = $"{channelName}_{broadcastId}_{timestamp}";
            var hash = HashHelper.GetSha1Hash(baseUrl);
            return $"{hash.Substring(0, 20).ToLower()}_{baseUrl}";
        }

        private static int PickDownloaderThreadCountByQualityAndTargetSpeed(FeedQuality quality, int? targetKbps) {
            /* Use a multiplier based on target speed */
            var speedMult = 1.0;
            if (targetKbps != null) {
                if (targetKbps < 1024) {
                    speedMult = 0.51;
                } else if (targetKbps < 5 * 1024) {
                    speedMult = 0.67;
                } else if (targetKbps < 15 * 1024) {
                    speedMult = 0.76;
                } else if (targetKbps < 30 * 1024) {
                    speedMult = 0.88;
                }
            }

            /* Use a base value from quality */
            switch (quality) {
                case FeedQuality.Q4Kp60:
                case FeedQuality.Q4Kp30:
                case FeedQuality.Q1440p60:
                case FeedQuality.Q1440p30:
                    return (int)(6 * speedMult);
                case FeedQuality.Q1080p60:
                case FeedQuality.Q1080p30:
                case FeedQuality.Source:
                    return (int)(8 * speedMult);
                case FeedQuality.Q720p60:
                case FeedQuality.Q720p30:
                    return (int)(10 * speedMult);
                case FeedQuality.Q480p60:
                case FeedQuality.Q480p30:
                    return (int)(12 * speedMult);
                case FeedQuality.Q360p60:
                case FeedQuality.Q360p30:
                    return (int)(14 * speedMult);
                default:
                    return (int)(20 * speedMult);
            }
        }
    }
}
