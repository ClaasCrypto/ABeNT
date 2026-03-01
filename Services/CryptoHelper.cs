using System;
using System.Security.Cryptography;
using System.Text;

namespace ABeNT.Services
{
    public static class CryptoHelper
    {
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }

        public static string Decrypt(string encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                return string.Empty;

            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);
            byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        /// <summary>
        /// Checks whether a value looks like DPAPI-encrypted Base64
        /// vs. a plain-text API key (which typically starts with recognisable prefixes
        /// or contains dashes/underscores that break valid Base64).
        /// </summary>
        public static bool IsEncrypted(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // DPAPI output is always valid Base64 and decodes to at least ~30+ bytes.
            if (value.Length < 40)
                return false;

            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                // DPAPI blobs are at least 30 bytes; plain API keys encoded as
                // Base64 would be much shorter after decoding.
                return bytes.Length >= 30;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
