using TwitchUnjail.Core.Managers;

namespace TwitchUnjail.Core.Utilities {
    
    public static class LogHelper {

        public static void LogIfAvailable(this FileLogManager logManager, string text) {
            if (logManager != null) {
                logManager.Log(text);
            }
        }
    }
}
