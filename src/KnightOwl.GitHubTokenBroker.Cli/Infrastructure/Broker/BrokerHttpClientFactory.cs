using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;

/// <summary>
/// Builds the <see cref="HttpClient"/> for a broker endpoint. This is the only
/// place either transport is mentioned.
/// </summary>
public static class BrokerHttpClientFactory
{
    /// <summary>
    /// How long a client waits. Generous enough to cover a cold mint against GitHub,
    /// short enough that a wedged broker fails a Git operation rather than hanging it.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Creates a client bound to the endpoint.</summary>
    /// <param name="endpoint">The broker endpoint to reach.</param>
    /// <returns>A client the caller owns and must dispose.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership transfers to the returned HttpClient, which is "
            + "constructed with disposeHandler: true and disposes the handler with "
            + "itself. Every path that does not reach that constructor disposes the "
            + "handler explicitly."
    )]
    public static HttpClient Create(BrokerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var socketPath = endpoint.UnixSocketPath;

        SocketsHttpHandler handler = new();
        try
        {
            // HTTP over a Unix socket: the request URI's host is never resolved,
            // because every connection goes to this path.
            handler.ConnectCallback = async (_, cancellationToken) =>
            {
                Socket socket = new(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified
                );

                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };

            // The client takes ownership of the handler, so disposing the client
            // disposes both.
            return new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = endpoint.BaseUri,
                Timeout = RequestTimeout,
            };
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }
}
