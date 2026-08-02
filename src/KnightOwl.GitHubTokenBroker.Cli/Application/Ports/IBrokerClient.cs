using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;


namespace KnightOwl.GitHubTokenBroker.Cli.Application.Ports;

/// <summary>
/// The client's view of the broker, so how it is reached changes nothing above
/// this line.
/// </summary>
public interface IBrokerClient
{
    /// <summary>Requests a token for one repository.</summary>
    /// <param name="repository">The repository to mint for.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The token and its expiry.</returns>
    Task<BrokerTokenResponse> RequestTokenAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    );

    /// <summary>Confirms reachability and authorization without minting.</summary>
    /// <param name="repository">The repository to check.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>The resolved repository and its configured ceiling.</returns>
    Task<BrokerCheckResponse> CheckAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    );
}
