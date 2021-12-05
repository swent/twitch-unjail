using System;
using System.Security.Cryptography;
using System.Text;

namespace TwitchUnjail.Core.Utilities {
    
    public static class HashHelper {
        
        public static string GetSha1Hash(string input) {
            using (var alg = HashAlgorithm.Create("SHA1")) {
                if (alg == null) {
                    throw new Exception("Unable to instantiate sha1 hash generator.");
                }
                
                var hash = alg.ComputeHash(Encoding.UTF8.GetBytes(input));
                
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) {
                    sb.Append(b.ToString("X2"));
                }

                return sb.ToString();
            }
        }
    }
}
