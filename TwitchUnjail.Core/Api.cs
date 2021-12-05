using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchUnjail.Core.Models.HttpRequests;
using TwitchUnjail.Core.Models.HttpResponses;
using TwitchUnjail.Core.Utilities;

namespace TwitchUnjail.Core {
    
    public static class Api {

        private const string ApiUrl = "https://api.twitch.tv";
        private const string TokenUrl = "https://gql.twitch.tv/gql";
        private const string TwitchClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

        public static async ValueTask<VideoInfoResponse> GetVideoInfo(string url) {
            var videoId = GetVideoIdForUrl(url);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", HttpHelper.UserAgent);
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.twitchtv.v5+json");
            client.DefaultRequestHeaders.Add("Client-ID", TwitchClientId);
            var response = await client.GetAsync($"{ApiUrl}/kraken/videos/{videoId}");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<VideoInfoResponse>(content);
        }

        public static async ValueTask<PlaybackAccessTokenResponse> GetPlaybackAccessToken(string id, bool isLive) {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", HttpHelper.UserAgent);
            client.DefaultRequestHeaders.Add("Client-ID", TwitchClientId);
            var response = await client.PostAsJsonAsync(TokenUrl, new PlaybackAccessTokenRequest(isLive, id));
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<PlaybackAccessTokenResponse>(await response.Content.ReadAsStringAsync());
        }

        public static long GetVideoIdForUrl(string url) {
            try {
                return long.Parse(url.Split('/').Last());
            } catch (Exception) {
                throw new Exception($"Unable to find vod-id in url: {url ?? "NULL"}");
            }
        }
    }
}
