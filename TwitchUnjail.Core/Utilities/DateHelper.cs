using System;

namespace TwitchUnjail.Core.Utilities {
    
    public static class DateHelper {

        public static long ToUnixTimestamp(this DateTime date) {
            return (long)(date - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalSeconds;
        }
    }
}
