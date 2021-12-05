using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TwitchUnjail.Core.Utilities {
    
    public static class FileSystemHelper {

        private static HashSet<char>? _invalidCharsHash;
        private static HashSet<char> InvalidCharsHash =>
            _invalidCharsHash ??= new HashSet<char>(Path.GetInvalidFileNameChars());

        public static string StripInvalidChars(string filename) {
            var result = new List<char>();

            foreach (var chr in filename) {
                if (!InvalidCharsHash.Contains(chr)) {
                    result.Add(chr);
                }
            }

            return new string(result.ToArray());
        }

        public static string EnsurePathWithoutTrailingDelimiter(string path) {
            if (path.EndsWith(Path.DirectorySeparatorChar)) {
                return path.Substring(0, path.Length - 1);
            }
            return path;
        }
    }
}
