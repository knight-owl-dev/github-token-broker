namespace KnightOwl.GitHubTokenBroker.Infrastructure.GitHub;

/// <summary>
/// Body of <c>POST /app/installations/{id}/access_tokens</c>.
/// </summary>
/// <remarks>
/// <see cref="Repositories"/> carries repository names without their owner: the
/// owner is implied by the installation. Exactly one name is ever sent.
/// </remarks>
public sealed record InstallationTokenRequest
{
    /// <summary>The single repository name the token is restricted to.</summary>
    public required IReadOnlyList<string> Repositories { get; init; }

    /// <summary>The permission ceiling being requested, never exceeded.</summary>
    public required IReadOnlyDictionary<string, string> Permissions { get; init; }
}
