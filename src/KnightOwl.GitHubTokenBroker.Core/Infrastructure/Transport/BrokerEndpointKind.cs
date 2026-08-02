namespace KnightOwl.GitHubTokenBroker.Infrastructure.Transport;

/// <summary>Which local transport reaches the broker.</summary>
public enum BrokerEndpointKind
{
    /// <summary>A Unix domain socket at an absolute filesystem path.</summary>
    UnixSocket = 1,

    /// <summary>A host and port reached over HTTP.</summary>
    Http = 2,
}
