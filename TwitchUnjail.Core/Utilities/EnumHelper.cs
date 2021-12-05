using System;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Core.Utilities {
    
    public static class EnumHelper {

        public static FeedQuality FeedQualityFromKey(string key) {
            return key?.ToLower() switch {
                "audio_only" => FeedQuality.AudioOnly,
                "144p30" => FeedQuality.Q144p30,
                "160p30" => FeedQuality.Q160p30,
                "360p30" => FeedQuality.Q360p30,
                "360p60" => FeedQuality.Q360p60,
                "480p30" => FeedQuality.Q480p30,
                "480p60" => FeedQuality.Q480p60,
                "720p30" => FeedQuality.Q720p30,
                "720p60" => FeedQuality.Q720p60,
                "1080p30" => FeedQuality.Q1080p30,
                "1080p60" => FeedQuality.Q1080p60,
                "1440p30" => FeedQuality.Q1440p30,
                "1440p60" => FeedQuality.Q1440p60,
                "4Kp30" => FeedQuality.Q4Kp30,
                "4Kp60" => FeedQuality.Q4Kp60,
                "chunked" => FeedQuality.Source,
                _ => throw new Exception($"Feed quality unknown: {key ?? "NULL"}")
            };
        }

        public static string ToKey(this FeedQuality quality) {
            var key = quality.ToString();
            return key switch {
                "AudioOnly" => "audio_only",
                "Source" => "chunked",
                _ => key.Substring(1).ToLower()
            };
        }
    }
}
