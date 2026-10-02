using System.Net.Http;

namespace StreetCorsa.Services.Ai
{
    /// <summary>
    /// The game's one HTTP client, for every AI request: connections are pooled and renewed now and then (a provider's
    /// address may move). No timeout of its own: each request carries its own cancellation (<see cref="Rules.AiRules"/>).
    /// </summary>
    public static class AiHttp
    {
        private static readonly Lazy<HttpClient> Client = new(() => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        });

        public static HttpClient Shared => Client.Value;
    }
}
