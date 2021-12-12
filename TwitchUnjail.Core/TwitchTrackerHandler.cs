using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwitchUnjail.Core.Models;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Core {
    
    public static class TwitchTrackerHandler {

        private static Regex TwitchTrackerRegex = new("twitchtracker\\.com\\/([a-z0-9_-]+)\\/streams\\/([0-9]+)", RegexOptions.IgnoreCase);

        public static async ValueTask<VodRecoveryInfo> RetrieveInfo(string url) {
            var matches = TwitchTrackerRegex.Match(url);
            
            if (matches.Success && matches.Groups.Count == 3) {
                try {
                    var channelName = matches.Groups[1].Value;
                    var broadcastId = long.Parse(matches.Groups[2].Value);

                    var twitchTrackerHtml = await HttpHelper.GetHttp(url);
                    var displayNameLine = twitchTrackerHtml.Substring(twitchTrackerHtml.IndexOf("id=\"app-title\""));
                    var displayName = displayNameLine.Substring(displayNameLine.IndexOf(">") + 1, displayNameLine.IndexOf("<") - displayNameLine.IndexOf(">") - 1).Trim();
                    var dateLine = twitchTrackerHtml.Substring(twitchTrackerHtml.IndexOf("stream-timestamp-dt to-dowdatetime"));
                    var dateString = dateLine.Substring(dateLine.IndexOf(">") + 1, dateLine.IndexOf("<") - dateLine.IndexOf(">") - 1).Trim();

                    return new VodRecoveryInfo {
                        Url = url,
                        BroadcastId = broadcastId,
                        ChannelName = channelName,
                        ChannelDisplayName = displayName,
                        RecordDate = DateTime.Parse(dateString, CultureInfo.InvariantCulture),
                    };
                } catch (Exception) {
                    throw new Exception("Url is not a valid twitchtracker.com stream url or the page structure has recently changed and is no longer readable.");
                }
            }
            throw new Exception("Url is not a valid twitchtracker.com stream url");
        }
    }
}
