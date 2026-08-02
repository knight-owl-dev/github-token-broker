namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Git;

/// <summary>
/// What a credential description turned out to be asking for.
/// </summary>
/// <remarks>
/// The distinction drives whether a decline is silent. Git may consult this helper
/// for hosts it does not serve, and complaining on every unrelated fetch would be
/// noise; a request for github.com over HTTPS that still cannot name a repository is
/// a configuration problem worth reporting.
/// </remarks>
public enum GitCredentialResolution
{
    /// <summary>A protocol or host this helper does not serve. Decline silently.</summary>
    NotServed = 1,

    /// <summary>
    /// Addressed to this helper, but no repository could be derived. Decline and say so.
    /// </summary>
    Unusable = 2,

    /// <summary>One repository was identified.</summary>
    Served = 3,
}
