namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;

/// <summary>
/// The version 1 routes. What every version shares is in
/// <see cref="BrokerProtocol"/>.
/// </summary>
public static class BrokerV1Routes
{
    /// <summary>Route that mints or returns a cached token.</summary>
    public const string TokenPath = "/v1/token";

    /// <summary>Route that reports authorization without minting.</summary>
    public const string CheckPath = "/v1/check";
}
