using System;
using System.Diagnostics;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Core.Utilities {
    
    public static class EnumHelper {

        public static FeedQuality FeedQualityFromKey(string key) {
            switch (key?.ToLower()) {
                case "audio_only":
                    return FeedQuality.AudioOnly;
                case "144p30":
                    return FeedQuality.Q144p30;
                case "160p30":
                    return FeedQuality.Q160p30;
                case "360p30":
                    return FeedQuality.Q360p30;
                case "360p60":
                    return FeedQuality.Q360p60;
                case "480p30":
                    return FeedQuality.Q480p30;
                case "480p60":
                    return FeedQuality.Q480p60;
                case "720p30":
                    return FeedQuality.Q720p30;
                case "720p60":
                    return FeedQuality.Q720p60;
                case "1080p30":
                    return FeedQuality.Q1080p30;
                case "1080p60":
                    return FeedQuality.Q1080p60;
                case "1440p30":
                    return FeedQuality.Q1440p30;
                case "1440p60":
                    return FeedQuality.Q1440p60;
                case "4Kp30":
                    return FeedQuality.Q4Kp30;
                case "4Kp60":
                    return FeedQuality.Q4Kp60;
                case "chunked":
                    return FeedQuality.Source;
                default:
                    throw new Exception($"Feed quality unknown: {key ?? "NULL"}");
            }
        }

        public static string ToKey(this FeedQuality quality) {
            var key = quality.ToString();
            if (key == "AudioOnly") {
                return "audio_only";
            }
            if (key == "Source") {
                return "chunked";
            }
            return key.Substring(1).ToLower();
        }
    }
}
