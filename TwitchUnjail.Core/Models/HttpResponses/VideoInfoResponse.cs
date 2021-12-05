using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwitchUnjail.Core.Models.HttpResponses {
    
    public class VideoInfoResponse {
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        [JsonPropertyName("game")]
        public string Game { get; set; }
        [JsonPropertyName("recorded_at")]
        public DateTime RecordDate { get; set; }
        [JsonPropertyName("length")]
        public int Length { get; set; }
        [JsonPropertyName("views")]
        public long Views { get; set; }
        [JsonPropertyName("muted_segments")]
        public VideoInfoResponseMutedSegment[] MutedSegments { get; set; }
        [JsonPropertyName("seek_previews_url")]
        public string SeekPreviewsUrl { get; set; }
        [JsonPropertyName("resolutions")]
        public Dictionary<string, string> Resolutions { get; set; }
        [JsonPropertyName("channel")]
        public VideoInfoResponseChannel Channel { get; set; }
    }

    public class VideoInfoResponseMutedSegment {
        
        [JsonPropertyName("duration")]
        public int Duration { get; set; }
        [JsonPropertyName("offset")]
        public long Offset { get; set; }
    }

    public class VideoInfoResponseChannel {
        
        [JsonPropertyName("_id")]
        public long Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; }
        [JsonPropertyName("language")]
        public string Language { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("views")]
        public long Views { get; set; }
        [JsonPropertyName("followers")]
        public long Followers { get; set; }
    }
}
