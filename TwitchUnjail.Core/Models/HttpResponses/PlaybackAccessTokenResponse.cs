using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchUnjail.Core.Models.HttpResponses {
    
    public class PlaybackAccessTokenResponse {
        
        [JsonPropertyName("data")]
        public PlaybackAccessTokenResponseData Data { get; set; }
        [JsonPropertyName("extensions")]
        public PlaybackAccessTokenResponseExtensions Extensions { get; set; }
    }

    public class PlaybackAccessTokenResponseData {
        
        [JsonPropertyName("videoPlaybackAccessToken")]
        public PlaybackAccessTokenResponseToken VideoPlaybackAccessToken { get; set; }
    }
    
    public class PlaybackAccessTokenResponseToken {
        
        [JsonPropertyName("value")]
        public string Value { get; set; }
        [JsonPropertyName("signature")]
        public string Signature { get; set; }
        [JsonPropertyName("__typename")]
        public string TypeName { get; set; }

        private PlaybackAccessTokenResponseTokenValue _parsedValue;
        public PlaybackAccessTokenResponseTokenValue ParsedValue {
            get {
                if (_parsedValue == null) {
                    _parsedValue = JsonSerializer.Deserialize<PlaybackAccessTokenResponseTokenValue>(Value);
                }
                return _parsedValue;
            }
        }
    }
    
    public class PlaybackAccessTokenResponseTokenValue {
        
        [JsonPropertyName("authorization")]
        public PlaybackAccessTokenResponseTokenValueAuthorization Authorization { get; set; }
        [JsonPropertyName("chansub")]
        public PlaybackAccessTokenResponseTokenValueChansub Chansub { get; set; }
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; }
        [JsonPropertyName("expires")]
        public long Expires { get; set; }
        [JsonPropertyName("https_required")]
        public bool HttpsRequired { get; set; }
        [JsonPropertyName("privileged")]
        public bool Privileged { get; set; }
        [JsonPropertyName("user_id")]
        public string UserId { get; set; }
        [JsonPropertyName("version")]
        public int Version { get; set; }
        [JsonPropertyName("vod_id")]
        public long VodId { get; set; }
    }
    
    public class PlaybackAccessTokenResponseTokenValueAuthorization {
        
        [JsonPropertyName("forbidden")]
        public bool Forbidden { get; set; }
        [JsonPropertyName("reason")]
        public string Reason { get; set; }
    }
    
    public class PlaybackAccessTokenResponseTokenValueChansub {
        
        [JsonPropertyName("restricted_bitrates")]
        public string[] RestrictedBitrates { get; set; }
    }

    public class PlaybackAccessTokenResponseExtensions {
        
        [JsonPropertyName("durationMilliseconds")]
        public long DurationMilliseconds { get; set; }
        [JsonPropertyName("operationName")]
        public string OperationName { get; set; }
        [JsonPropertyName("requestID")]
        public string RequestId { get; set; }
    }
}
