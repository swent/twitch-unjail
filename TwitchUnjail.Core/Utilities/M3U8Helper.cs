using System;
using System.Linq;
using System.Threading.Tasks.Dataflow;

namespace TwitchUnjail.Core.Utilities {
    
    public static class M3U8Helper {

        public static string[][] ExtractLinks(string m3U8, string baseUrl) {
            var chunks = MapToBaseUrl(m3U8, baseUrl).Split("#EXT-X-DISCONTINUITY")
                .Select(part => part.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && line[0] != '#')
                    .ToArray())
                .ToArray();

            return chunks;
        }

        public static string MapToBaseUrl(string m3U8, string baseUrl) {
            var lines = m3U8.Split('\n')
                .Select(line => {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line) && line[0] != '#' && line.EndsWith(".ts")) {
                        line = $"{baseUrl}/{line.Replace("unmuted.ts", "muted.ts")}";
                    }
                    return line;
                });
            return string.Join(Environment.NewLine, lines);
        }
    }
}
