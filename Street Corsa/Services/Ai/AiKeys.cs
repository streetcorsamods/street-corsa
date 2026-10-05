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

        /// <summary>
        /// The saved key, only for the provider and address it was typed for: a key for Anthropic or OpenRouter never goes to
        /// another provider or address the player switches to, and never to Ollama. A key saved before keys were tied to one
        /// belongs to the provider and address it was saved with. Null when there is none for them.
        /// </summary>
        public static string? KeyFor(Services.Settings.AiSettings settings, string? provider, string? endpoint)
        {
            var kind = AiProviders.Normalize(provider);
            if (kind == AiProviders.Ollama || string.IsNullOrWhiteSpace(settings.ApiKeyProtected)) return null;

            var (keyProvider, keyEndpoint) = settings.KeyProvider is { Length: > 0 }
                ? (settings.KeyProvider, settings.KeyEndpoint)
                : (settings.TextProvider, settings.Endpoint);
            if (AiProviders.Normalize(keyProvider) != kind || !AiProviders.SameAddress(kind, keyEndpoint, endpoint)) return null;
            return Unprotect(settings.ApiKeyProtected);
        }

        /// <summary>
        /// The key of <paramref name="settings"/> tied to the provider and address it belongs to, when it was saved before keys
        /// were: those of the settings it was saved with
        /// </summary>
        public static void Tie(Services.Settings.AiSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKeyProtected) || settings.KeyProvider is { Length: > 0 }) return;
            settings.KeyProvider = AiProviders.Normalize(settings.TextProvider);
            settings.KeyEndpoint = settings.Endpoint;
        }
    }
}
