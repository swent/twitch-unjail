using System;

namespace TwitchUnjail.Core.Models {
    
    public class VodRecoveryInfo {
        
        public string Url { get; set; }
        public string ChannelName { get; set; }
        public string ChannelDisplayName { get; set; }
        public long BroadcastId { get; set; }
        public DateTime RecordDate { get; set; }
    }
}
