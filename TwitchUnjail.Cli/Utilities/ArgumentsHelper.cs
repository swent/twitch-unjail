using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Cli.Utilities {
    
    public static class ArgumentsHelper {

        public static FeedQuality QualityFromString(string input) {
            switch (input?.ToLower()) {
                case "audioonly":
                    return FeedQuality.AudioOnly;
                case "144p":
                case "144p30":
                    return FeedQuality.Q144p30;
                case "160p":
                case "160p30":
                    return FeedQuality.Q160p30;
                case "360p":
                case "360p30":
                    return FeedQuality.Q360p30;
                case "360p60":
                    return FeedQuality.Q360p60;
                case "480p":
                case "480p30":
                    return FeedQuality.Q480p30;
                case "480p60":
                    return FeedQuality.Q480p60;
                case "720p":
                case "720p30":
                    return FeedQuality.Q720p30;
                case "720p60":
                    return FeedQuality.Q720p60;
                case "1080p":
                case "1080p30":
                    return FeedQuality.Q1080p30;
                case "1080p60":
                    return FeedQuality.Q1080p60;
                case "1440p":
                case "1440p30":
                    return FeedQuality.Q1440p30;
                case "1440p60":
                    return FeedQuality.Q1440p60;
                case "4k":
                case "4k30":
                    return FeedQuality.Q4Kp30;
                case "4kp60":
                    return FeedQuality.Q4Kp60;
                case "source":
                    return FeedQuality.Source;
                default:
                    throw new Exception($"Unknown quality setting: '{input ?? "NULL"}'");
            }
        }

        public static string QualityEnumToDisplayText(FeedQuality quality) {
            var text = quality.ToString();
            if (text.StartsWith("Q")) {
                return text.Substring(1);
            }
            return text;
        }

        public static T SearchArgument<T>(string[] args, string key) {
            string type;
            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?)) {
                type = "bool";
            } else if (typeof(T) == typeof(int) || typeof(T) == typeof(int?)) {
                type = "int";
            } else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?)) {
                type = "double";
            } else if (typeof(T) == typeof(string)) {
                type = "string";
            } else {
                throw new Exception($"Unknown data type requested to parse: {typeof(T)}");
            }
            
            for (var i = 0; i < args.Length; i++) {
                if (args[i].StartsWith("-")) {
                    var argKey = args[i].Substring(args[i][1] == '-' ? 2 : 1);
                    if (!argKey.Equals(key)) continue;
                    switch (type) {
                        case "bool":
                            if (args.Length - 1 > i && !args[i + 1].StartsWith("-")) {
                                if (args[i + 1] == "true") {
                                    return (T)(object)true;
                                } if (args[i + 1] == "false") {
                                    return (T)(object)false;
                                }
                                throw new Exception($"Invalid value for argument '{args[i]}': {args[i + 1]}");
                            } else {
                                return (T)(object)true;
                            }
                        case "int":
                            if (args.Length - 1 > i && !args[i + 1].StartsWith("-")) {
                                if (int.TryParse(args[i + 1], out var intValue)) {
                                    return (T)(object)intValue;
                                }
                                throw new Exception($"Invalid value for argument '{args[i]}': {args[i + 1]}");
                            } else {
                                throw new Exception($"No value given for argument '{args[i]}'");
                            }
                        case "double":
                            if (args.Length - 1 > i && !args[i + 1].StartsWith("-")) {
                                if (double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)) {
                                    return (T)(object)floatValue;
                                }
                                throw new Exception($"Invalid value for argument '{args[i]}': {args[i + 1]}");
                            } else {
                                throw new Exception($"No value given for argument '{args[i]}'");
                            }
                        case "string":
                            if (args.Length - 1 > i && !args[i + 1].StartsWith("-")) {
                                return (T)(object)args[i + 1];
                            } else {
                                throw new Exception($"No value given for argument '{args[i]}'");
                            }
                    }
                }
            }

            return default;
        }
    }
}
