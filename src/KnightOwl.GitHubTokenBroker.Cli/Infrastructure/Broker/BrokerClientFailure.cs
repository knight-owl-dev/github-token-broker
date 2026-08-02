namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;

/// <summary>Why a broker request did not produce a result.</summary>
public enum BrokerClientFailure
{
    /// <summary>
    /// The broker could not be reached, or did not answer in time.
    /// </summary>
    Unavailable = 1,

    /// <summary>
    /// The broker refused the repository. This is the expected answer for a
    /// repository the operator did not allowlist, and is not an error condition for
    /// the credential helper.
    /// </summary>
    Refused = 2,

    /// <summary>
    /// The broker answered, but could not supply a token. Its own log holds the
    /// reason.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The repository is allowlisted but the App installation does not grant it.
    /// Distinct from <see cref="Unavailable"/> because retrying cannot help, and
    /// from <see cref="Refused"/> because an operator has something to fix.
    /// </summary>
    Misconfigured = 4,
}
