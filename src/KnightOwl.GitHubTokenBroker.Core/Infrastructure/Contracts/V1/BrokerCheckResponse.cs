namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;

/// <summary>
/// Body of a successful <c>POST /v1/check</c>. Confirms reachability and
/// authorization and echoes the configured ceiling, without minting.
/// </summary>
public sealed record BrokerCheckResponse
{
    /// <summary>The canonical repository the broker resolved.</summary>
    public required string Repository { get; init; }

    /// <summary>The configured permission ceiling, in canonical form.</summary>
    public required string Permissions { get; init; }
}
