using System.IO;
using System.Net.Http;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The game's one HTTP client, for every AI request: connections are pooled and renewed now and then (a provider's
    /// address may move). No timeout of its own: each request carries its own cancellation (<see cref="Rules.AiRules"/>).
    /// Redirects are not followed: .NET takes only the Authorization header off a redirected request, so a key sent in a
    /// header of its own (Anthropic's x-api-key) would go on to whatever host a 3xx names, plain http included.
    /// </summary>
    public static class AiHttp
    {
        private static readonly Lazy<HttpClient> Client = new(() => new HttpClient(NewHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        });

        public static HttpClient Shared => Client.Value;

        /// <summary>The handler under <see cref="Shared"/></summary>
        internal static SocketsHttpHandler NewHandler() => new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AllowAutoRedirect = false
        };

        /// <summary>
        /// Throws when <paramref name="response"/> is a redirect, which is never followed: an address that moved is the
        /// player's to type again, so it is an answer the game cannot use, not a provider out of reach
        /// </summary>
        public static void NotRedirected(HttpResponseMessage response)
        {
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
                throw new AiProviderException($"HTTP {status}: the address sends the game on to {AiProviders.PlaceOf(response.Headers.Location)}, which is not followed; " +
                                              "type the address it moved to");
        }

        /// <summary><see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, a redirect thrown as one the game cannot use (<see cref="NotRedirected"/>)</summary>
        public static void EnsureSuccess(HttpResponseMessage response)
        {
            NotRedirected(response);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// The body of <paramref name="response"/> (asked with <see cref="HttpCompletionOption.ResponseHeadersRead"/>), read
        /// up to <paramref name="most"/> bytes: a provider or ComfyUI that sends more is no answer, and is not held in memory
        /// </summary>
        public static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, int most, CancellationToken cancellationToken)
        {
            NotRedirected(response);
            if (response.Content.Headers.ContentLength is { } length && length > most) throw TooLarge(most);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var read = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (read.Length + count > most) throw TooLarge(most);
                read.Write(buffer, 0, count);
            }

            return read.ToArray();
        }

        /// <summary>The body as text (UTF-8, as the AI APIs answer; a byte order mark left out), read up to <paramref name="most"/> bytes</summary>
        public static async Task<string> ReadTextAsync(HttpResponseMessage response, int most, CancellationToken cancellationToken)
        {
            var bytes = await ReadBytesAsync(response, most, cancellationToken).ConfigureAwait(false);
            var bom = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
            return System.Text.Encoding.UTF8.GetString(bytes, bom, bytes.Length - bom);
        }

        private static AiProviderException TooLarge(int most) => new($"An answer larger than {most / (1024 * 1024)} MB: left unread");
    }
}
