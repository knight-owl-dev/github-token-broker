using System.Diagnostics.CodeAnalysis;
using System.Net;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Service.Application;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;

/// <summary>
/// Builds the <see cref="HttpClient"/> the broker reaches GitHub with. One
/// long-lived client for one host.
/// </summary>
internal static class GitHubHttpClientFactory
{
    /// <summary>
    /// Keeps DNS from going stale in a process that runs for weeks.
    /// </summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Creates the client for a configured API root.</summary>
    /// <param name="apiBaseUri">The configured API root.</param>
    /// <returns>A client the caller owns and must dispose.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership transfers to the returned HttpClient, which is "
            + "constructed with disposeHandler: true and disposes the handler with "
            + "itself. Every path that does not reach that constructor disposes the "
            + "handler explicitly."
    )]
    public static HttpClient Create(Uri apiBaseUri)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUri);

        // A trailing slash keeps a relative request path from replacing the last
        // segment of a configured API path. Built before anything is allocated, so a
        // rejected URI cannot abandon a handler.
        var baseUrl = apiBaseUri.AbsoluteUri;
        var baseAddress = baseUrl.EndsWith('/') ? new Uri(baseUrl) : new Uri($"{baseUrl}/");

        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = ConnectionLifetime,
            AutomaticDecompression = DecompressionMethods.All,

            // Minting never legitimately redirects, so following one would only
            // ever take the request somewhere it was not addressed.
            AllowAutoRedirect = false,
        };

        try
        {
            // The client takes ownership of the handler, so disposing the client
            // disposes both.
            HttpClient client = new(handler, disposeHandler: true)
            {
                BaseAddress = baseAddress,
                Timeout = BrokerProtocol.MintAttemptTimeout,
            };

            // GitHub requires a User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(Globals.AppName);
            return client;
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }
}
