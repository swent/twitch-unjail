using System.Globalization;
using System.Text.RegularExpressions;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Core {
    
    public static partial class StreamsChartsHandler {
        
        [GeneratedRegex("streamscharts\\.com\\/channels\\/([a-z0-9_-]+)\\/streams\\/([0-9]+)", RegexOptions.IgnoreCase)]
        private static partial Regex StreamsChartsRegex();
        
        public static async ValueTask<VodRecoveryInfo> RetrieveInfo(string url) {
            var matches = StreamsChartsRegex().Match(url);

            if (!matches.Success || matches.Groups.Count != 3) {
                throw new Exception("Url is not a valid streamscharts.com stream url");
            }

            try {
                var channelName = matches.Groups[1].Value;
                var broadcastId = long.Parse(matches.Groups[2].Value);

                var streamsChartsHtml = await HttpHelper.GetHttp(url);
                var displayNameLine = streamsChartsHtml.Substring(streamsChartsHtml.IndexOf("<title>"));
                var displayName = displayNameLine.Substring(displayNameLine.IndexOf(">") + 1, displayNameLine.IndexOf(" stream analytics") - displayNameLine.IndexOf(">") - 1).Trim();
                var dateLine = streamsChartsHtml.Substring(streamsChartsHtml.IndexOf("<time class=\"") + 10);
                var dateStringParts = dateLine.Substring(dateLine.IndexOf("datetime=\"") + 10, dateLine.IndexOf("\">") - dateLine.IndexOf("datetime=\"") - 10).Trim().Split(' ');

                return new VodRecoveryInfo {
                    Url = url,
                    BroadcastId = broadcastId,
                    ChannelName = channelName,
                    ChannelDisplayName = displayName,
                    RecordDate = DateTime.Parse($"{string.Join("-", dateStringParts[0].Split('-').Reverse())} {dateStringParts[1]}", CultureInfo.InvariantCulture),
                };
            } catch (Exception) {
                throw new Exception("Url is not a valid streamscharts.com stream url or the page structure has recently changed and is no longer readable.");
            }
        }
    }
}
