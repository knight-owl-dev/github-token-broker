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
    /// The broker and GitHub disagree about what it may mint: the repository is
    /// allowlisted but not installed, or the App credential was rejected. Distinct
    /// from <see cref="Retryable"/> because it persists until an operator acts, and
    /// from <see cref="Refused"/> because there is something to fix.
    /// </summary>
    Misconfigured = 4,

    /// <summary>
    /// The broker answered but could not reach GitHub. Distinct from
    /// <see cref="Failed"/> because retrying can help, and from
    /// <see cref="Unavailable"/> because the broker itself is reachable.
    /// </summary>
    Retryable = 5,
}
