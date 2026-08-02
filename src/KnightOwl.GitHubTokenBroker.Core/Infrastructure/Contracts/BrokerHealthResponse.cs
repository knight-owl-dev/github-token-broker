namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;

/// <summary>
/// Body of <c>GET /health</c>. Reports liveness and allowlist size; never mints
/// and never names a repository.
/// </summary>
public sealed record BrokerHealthResponse
{
    /// <summary>Liveness marker for a reachable broker.</summary>
    public required string Status { get; init; }

    /// <summary>How many repositories the running configuration allowlists.</summary>
    public required int Repositories { get; init; }
}
