using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Resilience;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;

/// <summary>
/// Attempts a mint again when GitHub failed in a way another attempt could clear.
/// </summary>
/// <remarks>
/// Which failures earn another attempt is knowledge about GitHub, so this decides
/// that and <see cref="RetryBudget"/> only runs the loop. What it buys is a push that
/// survives a reset connection or a bad node behind GitHub's load balancer.
/// </remarks>
internal sealed class RetryingTokenIssuer : IInstallationTokenIssuer
{
    private readonly IInstallationTokenIssuer _inner;
    private readonly RetryBudget _budget;
    private readonly ILogger<RetryingTokenIssuer> _logger;

    /// <summary>Wraps an issuer.</summary>
    /// <param name="inner">The issuer that reaches GitHub.</param>
    /// <param name="budget">Bounds how long fresh attempts keep starting.</param>
    /// <param name="logger">Receives one record per attempt made again.</param>
    public RetryingTokenIssuer(
        IInstallationTokenIssuer inner,
        RetryBudget budget,
        ILogger<RetryingTokenIssuer> logger
    )
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _budget = budget;
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The last attempt's failure is the one that propagates, so an
    /// <see cref="TokenIssuanceFailure.AppUnauthorized"/> met on the way still drops
    /// the cache.
    /// </remarks>
    public Task<InstallationToken> IssueAsync(
        RepositoryAccessPolicy policy,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(policy);

        return _budget.RunAsync(
            token => _inner.IssueAsync(policy, token),
            (TokenIssuanceException failure) => IsWorthRetrying(failure, policy),
            cancellationToken
        );
    }

    /// <summary>Reports whether another attempt could do better.</summary>
    /// <param name="failure">The failure the attempt earned.</param>
    /// <param name="policy">What was being minted, for the log.</param>
    /// <returns><see langword="true"/> when another attempt is worth starting.</returns>
    /// <remarks>
    /// <see cref="TokenIssuanceFailure.Unavailable"/> alone.
    /// <see cref="TokenIssuanceFailure.TimedOut"/> has already used more than the
    /// budget holds, <see cref="TokenIssuanceFailure.RateLimited"/> clears on a scale
    /// of minutes, and every other class waits on an operator.
    /// </remarks>
    private bool IsWorthRetrying(TokenIssuanceException failure, RepositoryAccessPolicy policy)
    {
        var retrying = failure.Failure == TokenIssuanceFailure.Unavailable;

        if (retrying)
        {
            TokenIssuerLog.RetryingMint(_logger, policy.Repository.FullName, failure.Failure);
        }

        return retrying;
    }
}
