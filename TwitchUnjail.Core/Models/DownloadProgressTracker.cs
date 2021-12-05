using System;
using System.ComponentModel;
using TwitchUnjail.Core.Managers;

namespace TwitchUnjail.Core.Models {
    
    public class DownloadProgressTracker {

        private Action<DownloadProgressUpdateEventArgs> _updateCallback;

        public DownloadProgressTracker(Action<DownloadProgressUpdateEventArgs> updateCallback) {
            _updateCallback = updateCallback;
        }

        public void SignalProgressUpdate(DownloadProgressUpdateEventArgs downloadProgressUpdateEventArgs) {
            _updateCallback(downloadProgressUpdateEventArgs);
        }
    }
}
