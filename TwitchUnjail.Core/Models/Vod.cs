using System;
using System.Collections.Generic;
using TwitchUnjail.Core.Models.Enums;

namespace TwitchUnjail.Core.Models {
    
    public class Vod {
        
        public long Id { get; }
        
        public string Title { get; }
        
        public string ChannelName { get; }
        
        public string ChannelDisplayName { get; }
        
        public DateTime RecordDate { get; }
        
        public int DurationSeconds { get; }
        
        public Dictionary<FeedQuality, string> Feeds { get; }

        public Vod(long id, string title, string channelName, string channelDisplayName, DateTime recordDate, int durationSeconds, Dictionary<FeedQuality, string> feeds) {
            Id = id;
            Title = title;
            ChannelName = channelName;
            ChannelDisplayName = channelDisplayName;
            RecordDate = recordDate;
            DurationSeconds = durationSeconds;
            Feeds = feeds;
        }
    }
}
