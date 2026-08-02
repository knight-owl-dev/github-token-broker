using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Transport;

/// <summary>
/// Where the client reaches the broker: <c>unix:///absolute/path/broker.sock</c>
/// or <c>http://host:port</c>. One abstraction covers both so Git and agent
/// behavior is identical whichever form a deployment selects.
/// </summary>
public sealed class BrokerEndpoint
{
    /// <summary>
    /// Authority used for request URIs over a Unix socket. The socket path
    /// determines the peer, so this host is never resolved.
    /// </summary>
    private const string UnixSocketRequestAuthority = "http://localhost";

    private BrokerEndpoint(BrokerEndpointKind kind, Uri baseUri, string? unixSocketPath)
    {
        this.Kind = kind;
        this.BaseUri = baseUri;
        this.UnixSocketPath = unixSocketPath;
    }

    /// <summary>Which transport this endpoint names.</summary>
    public BrokerEndpointKind Kind { get; }

    /// <summary>Base address for request URIs on either transport.</summary>
    public Uri BaseUri { get; }

    /// <summary>Socket path when <see cref="Kind"/> is a Unix socket.</summary>
    public string? UnixSocketPath { get; }

    /// <summary>Parses an endpoint, throwing when it is unusable.</summary>
    /// <param name="value">A <c>unix://</c> or <c>http://</c> endpoint.</param>
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
    /// <param name="value">A <c>unix://</c> or <c>http://</c> endpoint.</param>
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
            "http" => TryParseHttp(uri, out endpoint, out error),

            // The broker's TCP listener serves plaintext, so an https endpoint would
            // parse and then never connect.
            "https" => Fail("endpoint scheme \"https\" is not served; the TCP listener is plaintext", out error),
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

        endpoint = new BrokerEndpoint(
            BrokerEndpointKind.UnixSocket,
            new Uri(UnixSocketRequestAuthority),
            path
        );

        error = null;
        return true;
    }

    private static bool TryParseHttp(
        Uri uri,
        [NotNullWhen(true)] out BrokerEndpoint? endpoint,
        [NotNullWhen(false)] out string? error
    )
    {
        endpoint = null;

        if (uri.AbsolutePath is not ("" or "/"))
        {
            error = "http endpoint must not carry a path";
            return false;
        }

        endpoint = new BrokerEndpoint(
            BrokerEndpointKind.Http,
            new Uri($"{uri.Scheme}://{uri.Authority}"),
            null
        );

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
        => this.Kind == BrokerEndpointKind.UnixSocket
            ? $"unix://{this.UnixSocketPath}"
            : this.BaseUri.ToString();
}
