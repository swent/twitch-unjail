using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TwitchUnjail.Core.Utilities {
    
    public static class FileSystemHelper {

        private static HashSet<char> _invalidCharsHash;
        private static HashSet<char> InvalidCharsHash =>
            _invalidCharsHash ??= new HashSet<char>(Path.GetInvalidFileNameChars());

        public static string StripInvalidChars(string filename) {
            return new string(filename.Where(chr => !InvalidCharsHash.Contains(chr)).ToArray());
        }

        public static string EnsurePathWithoutTrailingDelimiter(string path) {
            return path.EndsWith(Path.DirectorySeparatorChar)
                ? path.Substring(0, path.Length - 1)
                : path;
        }
    }
}
