using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;


namespace KnightOwl.GitHubTokenBroker.Service.Application.Ports;

/// <summary>
/// Mints one installation token for exactly one allowlisted repository at no more
/// than that repository's configured ceiling. The application depends on this
/// port; the GitHub HTTP adapter implements it.
/// </summary>
public interface IInstallationTokenIssuer
{
    /// <summary>Mints a token for one repository at its configured ceiling.</summary>
    /// <param name="policy">The repository and the ceiling it is granted.</param>
    /// <param name="cancellationToken">Abandons the mint.</param>
    /// <returns>The token and its expiry.</returns>
    /// <exception cref="TokenIssuanceException">The mint failed, classified.</exception>
    Task<InstallationToken> IssueAsync(
        RepositoryAccessPolicy policy,
        CancellationToken cancellationToken
    );
}
