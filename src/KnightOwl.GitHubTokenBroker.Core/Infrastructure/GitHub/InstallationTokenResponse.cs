namespace KnightOwl.GitHubTokenBroker.Infrastructure.GitHub;

/// <summary>
/// Body of <c>POST /app/installations/{id}/access_tokens</c>.
/// </summary>
/// <remarks>
/// Every member is optional on the wire so a malformed response fails explicit
/// validation rather than surfacing as a deserialization exception.
/// </remarks>
public sealed record InstallationTokenResponse
{
    /// <summary>Treated as opaque: no prefix, format, or length is assumed.</summary>
    public string? Token { get; init; }

    /// <summary>When GitHub says the token stops working.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Permissions actually granted, which the broker checks against the ceiling.</summary>
    public Dictionary<string, string>? Permissions { get; init; }

    /// <summary>Whether the token covers selected repositories or all of them.</summary>
    public string? RepositorySelection { get; init; }

    /// <summary>The repositories the token covers.</summary>
    public IReadOnlyList<InstallationRepository>? Repositories { get; init; }
}
