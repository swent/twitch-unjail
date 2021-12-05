using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Core {
    
    public static class StreamsChartsHandler {
        
        public static async ValueTask<VodRecoveryInfo> RetrieveInfo(string streamsChartsUrl) {
            if (streamsChartsUrl.StartsWith("https://streamscharts.com/")) {
                var parts = streamsChartsUrl.Split('/');
                if (parts.Length >= 7) {
                    try {
                        var channelName = parts.Skip(4).First();
                        var broadcastId = long.Parse(parts.Last());

                        var streamsChartsHtml = await HttpHelper.GetHttp(streamsChartsUrl);
                        var displayNameLine = streamsChartsHtml.Substring(streamsChartsHtml.IndexOf("<title>"));
                        var displayName = displayNameLine.Substring(displayNameLine.IndexOf(">") + 1, displayNameLine.IndexOf(" stream analytics") - displayNameLine.IndexOf(">") - 1).Trim();
                        var dateLine = streamsChartsHtml.Substring(streamsChartsHtml.IndexOf("<time class=\"") + 10);
                        var dateStringParts = dateLine.Substring(dateLine.IndexOf("datetime=\"") + 10, dateLine.IndexOf("\">") - dateLine.IndexOf("datetime=\"") - 10).Trim().Split(' ');

                        return new VodRecoveryInfo {
                            Url = streamsChartsUrl,
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
            throw new Exception("Url is not a valid streamscharts.com stream url");
        }
    }
}
