using System.Text.Json.Serialization;

namespace TwitchUnjail.Core.Models.HttpRequests {
    
    public class PlaybackAccessTokenRequest {

        [JsonPropertyName("operationName")]
        public string OperationName { get; set; }
        [JsonPropertyName("variables")]
        public PlaybackAccessTokenRequestVariables Variables { get; set; }
        [JsonPropertyName("extensions")]
        public PlaybackAccessTokenRequestExtensions Extensions { get; set; }

        public PlaybackAccessTokenRequest(bool isLive, string id) {
            OperationName = "PlaybackAccessToken";
            Variables = new PlaybackAccessTokenRequestVariables(isLive, id);
            Extensions = new PlaybackAccessTokenRequestExtensions();
        }
    }

    public class PlaybackAccessTokenRequestVariables {
        
        [JsonPropertyName("isLive")]
        public bool IsLive { get; set; }
        [JsonPropertyName("login")]
        public string Login { get; set; }
        [JsonPropertyName("isVod")]
        public bool IsVod { get; set; }
        [JsonPropertyName("vodID")]
        public string VodId { get; set; }
        [JsonPropertyName("playerType")]
        public string PlayerType { get; set; }

        public PlaybackAccessTokenRequestVariables(bool isLive, string id) {
            IsLive = isLive;
            Login = isLive ? id : string.Empty;
            IsVod = !isLive;
            VodId = isLive ? string.Empty : id;
            PlayerType = "channel_home_live";
        }
    }
    
    public class PlaybackAccessTokenRequestExtensions {
        
        [JsonPropertyName("persistedQuery")]
        public PlaybackAccessTokenRequestPersistedQuery PersistedQuery { get; set; }

        public PlaybackAccessTokenRequestExtensions() {
            PersistedQuery = new PlaybackAccessTokenRequestPersistedQuery();
        }
    }
    
    public class PlaybackAccessTokenRequestPersistedQuery {
        
        [JsonPropertyName("version")]
        public int Version { get; set; }
        [JsonPropertyName("sha256Hash")]
        public string Sha256Hash { get; set; }

        public PlaybackAccessTokenRequestPersistedQuery() {
            Version = 1;
            Sha256Hash = "0828119ded1c13477966434e15800ff57ddacf13ba1911c129dc2200705b0712";
        }
    }
}
