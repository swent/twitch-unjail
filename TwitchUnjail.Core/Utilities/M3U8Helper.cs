using System.Linq;

namespace TwitchUnjail.Core.Utilities {
    
    public static class M3U8Helper {

        public static string[][] ExtractLinks(string m3U8, string baseUrl) {
            var chunks = m3U8.Split("#EXT-X-DISCONTINUITY")
                .Select(part => part.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && line[0] != '#')
                    .Select(line => $"{baseUrl}/{line.Replace("unmuted.ts", "muted.ts")}")
                    .ToArray())
                .ToArray();

            return chunks;
        }
    }
}
