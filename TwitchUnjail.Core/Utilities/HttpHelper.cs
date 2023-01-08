using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TwitchUnjail.Core.Utilities {
    
    public static class HttpHelper {

        public const string DomainsUrl = "https://raw.githubusercontent.com/TwitchRecover/TwitchRecover/master/domains.txt";
        public const string UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/108.0.5359.112 Mobile/15E148 Safari/604.1";

        public static async Task<bool> IsUrlReachable(string url) {
            try {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                client.DefaultRequestHeaders.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return true;
            } catch (Exception) {
                return false;
            }
        }

        public static async ValueTask<string[]> GetTwitchDomains() {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            var response = await client.GetAsync(DomainsUrl);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadAsStringAsync())
                .Split('\n')
                .Select(d => d.Trim())
                .ToArray();
        }
        
        public static async ValueTask<string> GetHttp(string url) {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
    }
}
