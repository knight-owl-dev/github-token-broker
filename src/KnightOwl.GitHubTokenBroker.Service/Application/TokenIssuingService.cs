using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;


namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// The token use case: serve a sufficiently fresh cached grant, or mint one.
/// </summary>
public sealed class TokenIssuingService
{
    private readonly InstallationTokenCache _cache;
    private readonly IInstallationTokenIssuer _issuer;

    /// <summary>Creates the service.</summary>
    /// <param name="cache">Holds grants for the process lifetime.</param>
    /// <param name="issuer">Mints a grant when the cache cannot serve one.</param>
    public TokenIssuingService(InstallationTokenCache cache, IInstallationTokenIssuer issuer)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(issuer);

        _cache = cache;
        _issuer = issuer;
    }

    /// <summary>Obtains a token for an already authorized policy.</summary>
    /// <param name="policy">The policy resolved from the allowlist.</param>
    /// <param name="cancellationToken">Abandons this caller's wait.</param>
    /// <returns>A token with more than the refresh margin remaining.</returns>
    /// <exception cref="TokenIssuanceException">The mint failed, classified.</exception>
    public async Task<InstallationToken> IssueAsync(RepositoryAccessPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        try
        {
            // The mint runs without the caller's token so one client giving up
            // cannot cancel work other clients are waiting on.
            return await _cache.GetOrIssueAsync(
                policy,
                candidate => _issuer.IssueAsync(candidate, CancellationToken.None),
                cancellationToken
            );
        }
        catch (TokenIssuanceException exception) when (exception.Failure == TokenIssuanceFailure.AppUnauthorized)
        {
            // The App credential itself was rejected, which a key rotation or
            // revocation causes. Every stored grant was minted with that
            // credential, so none of them is trustworthy.
            _cache.Clear();
            throw;
        }
    }
}
