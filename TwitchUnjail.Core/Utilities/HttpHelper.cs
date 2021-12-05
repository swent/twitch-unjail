using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TwitchUnjail.Core.Utilities {
    
    public static class HttpHelper {

        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.36 Safari/537.36";

        public static async Task<bool> IsUrlReachable(string url) {
            try {
                using (var client = new HttpClient()) {
                    client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    client.DefaultRequestHeaders.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    return true;
                }
            } catch (Exception) {
                return false;
            }
        }

        public static async ValueTask<string[]> GetTwitchDomains() {
            using (var client = new HttpClient()) {
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetAsync("https://raw.githubusercontent.com/TwitchRecover/TwitchRecover/main/domains.txt");
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadAsStringAsync())
                    .Split('\n')
                    .Select(d => d.Trim())
                    .ToArray();
            }
        }
        
        public static async ValueTask<string> GetHttp(string url) {
            using (var client = new HttpClient()) {
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }
    }
}
