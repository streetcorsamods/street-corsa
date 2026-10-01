using System.Security.Cryptography;
using System.Text;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The player's API key, encrypted with Windows DPAPI for the current user (<see cref="ProtectedData"/>): settings.json
    /// holds only the encrypted bytes as base64, which another Windows user or another PC cannot read back. An entropy
    /// of the game's own keeps another program of the same user from reading it by accident.
    /// </summary>
    public static class AiKeys
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreetCorsa.Ai.ApiKey");

        /// <summary>The key encrypted, as settings.json keeps it; null for no key</summary>
        public static string? Protect(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>The key back; null for none, or one that cannot be read (another user's, another PC's, edited by hand)</summary>
        public static string? Unprotect(string? protectedKey)
        {
            if (string.IsNullOrWhiteSpace(protectedKey)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return null;
            }
        }
    }
}
