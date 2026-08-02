using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Transport;

/// <summary>
/// Where the client reaches the broker: <c>unix:///absolute/path/broker.sock</c>.
/// </summary>
/// <remarks>
/// A URI rather than a bare path, so a second transport arrives as a new scheme
/// instead of breaking the endpoint clients already set.
/// </remarks>
public sealed class BrokerEndpoint
{
    /// <summary>
    /// Authority used for request URIs over a Unix socket. The socket path
    /// determines the peer, so this host is never resolved.
    /// </summary>
    private const string UnixSocketRequestAuthority = "http://localhost";

    private BrokerEndpoint(Uri baseUri, string unixSocketPath)
    {
        this.BaseUri = baseUri;
        this.UnixSocketPath = unixSocketPath;
    }

    /// <summary>Base address for request URIs.</summary>
    public Uri BaseUri { get; }

    /// <summary>The socket path to connect to.</summary>
    public string UnixSocketPath { get; }

    /// <summary>Parses an endpoint, throwing when it is unusable.</summary>
    /// <param name="value">A <c>unix://</c> endpoint.</param>
    /// <returns>The parsed endpoint.</returns>
    /// <exception cref="FormatException">The value does not name a supported endpoint.</exception>
    public static BrokerEndpoint Parse(string? value)
    {
        if (TryParse(value, out var endpoint, out var error))
        {
            return endpoint;
        }

        throw new FormatException(error);
    }

    /// <summary>Parses an endpoint without throwing.</summary>
    /// <param name="value">A <c>unix://</c> endpoint.</param>
    /// <param name="endpoint">The parsed endpoint when parsing succeeds.</param>
    /// <param name="error">Why the value was refused, when parsing fails.</param>
    /// <returns><see langword="true"/> when the value names a supported endpoint.</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out BrokerEndpoint? endpoint,
        [NotNullWhen(false)] out string? error
    )
    {
        endpoint = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "endpoint is empty";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            error = "endpoint is not an absolute URI";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "endpoint must not carry a query or fragment";
            return false;
        }

        return uri.Scheme switch
        {
            "unix" => TryParseUnix(uri, out endpoint, out error),

            // Named rather than left to the catch-all: both are plausible enough
            // that "unsupported scheme" would read as a typo. Naming them also
            // reserves the meaning, should a TLS transport want https back.
            "http" or "https" => Fail(
                $"endpoint scheme \"{uri.Scheme}\" is not served; the broker listens on a Unix socket",
                out error
            ),
            _ => Fail($"unsupported endpoint scheme \"{uri.Scheme}\"", out error),
        };
    }

    private static bool TryParseUnix(
        Uri uri,
        [NotNullWhen(true)] out BrokerEndpoint? endpoint,
        [NotNullWhen(false)] out string? error
    )
    {
        endpoint = null;

        if (!string.IsNullOrEmpty(uri.Host))
        {
            error = "unix endpoint must not name a host";
            return false;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!Path.IsPathRooted(path))
        {
            error = "unix endpoint path must be absolute";
            return false;
        }

        endpoint = new BrokerEndpoint(new Uri(UnixSocketRequestAuthority), path);

        error = null;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"unix://{this.UnixSocketPath}";
}
