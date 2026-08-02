namespace KnightOwl.GitHubTokenBroker.Infrastructure.GitHub;

/// <summary>
/// One repository entry in an installation token response, used to confirm the
/// grant covers exactly the repository that was requested.
/// </summary>
public sealed record InstallationRepository
{
    /// <summary>Stable numeric repository identifier.</summary>
    public long? Id { get; init; }

    /// <summary>Repository name without its owner.</summary>
    public string? Name { get; init; }

    /// <summary>Repository name in <c>OWNER/REPOSITORY</c> form.</summary>
    public string? FullName { get; init; }
}
