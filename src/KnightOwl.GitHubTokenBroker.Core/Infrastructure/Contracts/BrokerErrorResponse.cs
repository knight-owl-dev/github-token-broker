namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;

/// <summary>
/// Client-facing failure body, emitted on every route including the unversioned
/// <c>/health</c>. Deliberately coarse: the broker logs a precise, token-free
/// diagnostic and returns a generic reason.
/// </summary>
public sealed record BrokerErrorResponse
{
    /// <summary>A short, non-revealing reason for the failure.</summary>
    public required string Error { get; init; }
}
