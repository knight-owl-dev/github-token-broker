using System.Diagnostics.CodeAnalysis;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// Turns an untrusted host and repository pair into an allowlisted policy, or
/// refuses it. Both <c>/v1/token</c> and <c>/v1/check</c> go through here, so
/// authorization cannot differ between what a client is told and what it is given.
/// </summary>
public sealed class RepositoryRequestValidator
{
    private readonly GitHubHost _host;
    private readonly RepositoryAllowlist _allowlist;

    /// <summary>Creates the validator for one running configuration.</summary>
    /// <param name="host">The host the broker serves.</param>
    /// <param name="allowlist">The repositories the broker will mint for.</param>
    public RepositoryRequestValidator(GitHubHost host, RepositoryAllowlist allowlist)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(allowlist);

        _host = host;
        _allowlist = allowlist;
    }

    /// <summary>Resolves a client request to the policy that governs it.</summary>
    /// <param name="requestedHost">Host named by the client.</param>
    /// <param name="requestedRepository">Repository named by the client.</param>
    /// <param name="policy">The governing policy when the request is authorized.</param>
    /// <param name="error">
    /// A precise server-side reason when the request is refused. It is logged, never
    /// returned, so a caller cannot distinguish an unlisted repository from a
    /// malformed one.
    /// </param>
    /// <returns><see langword="true"/> when the request is authorized.</returns>
    public bool TryResolve(
        string? requestedHost,
        string? requestedRepository,
        [NotNullWhen(true)] out RepositoryAccessPolicy? policy,
        [NotNullWhen(false)] out string? error
    )
    {
        policy = null;

        if (!GitHubHost.TryParse(requestedHost, out var parsedHost, out var hostError))
        {
            error = hostError;
            return false;
        }

        if (!parsedHost.Equals(_host))
        {
            error = $"host \"{parsedHost.Name}\" is not the host this broker serves";
            return false;
        }

        if (!RepositoryName.TryParse(requestedRepository, out var repository, out var repositoryError))
        {
            error = repositoryError;
            return false;
        }

        if (!_allowlist.TryResolve(repository, out policy))
        {
            error = $"repository \"{repository.Key}\" is not allowlisted";
            return false;
        }

        error = null;
        return true;
    }
}
