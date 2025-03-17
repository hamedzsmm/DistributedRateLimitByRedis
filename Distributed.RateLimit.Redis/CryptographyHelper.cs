using System.Security.Cryptography;
using System.Text;

namespace Distributed.RateLimit.Redis
{
    public static class CryptographyHelper
    {
        public static string GetSha256Hash(this string inputString)
        {
            using HashAlgorithm algorithm = SHA256.Create();
            var hashBytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));

            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }
    }
}
