namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;

/// <summary>
/// The part of the client/broker contract that outlives any one version: the
/// liveness route and request limits. Versioned routes and bodies live under
/// <c>Contracts.V1</c>.
/// </summary>
public static class BrokerProtocol
{
    /// <summary>Liveness route. Never mints.</summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Largest accepted request body. A request is two short strings, so this is
    /// generous while still bounding an untrusted local caller.
    /// </summary>
    public const int MaxRequestBytes = 4096;
}
