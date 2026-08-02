using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Domain.Access;

/// <summary>
/// One allowlisted repository and the permission ceiling the operator granted it.
/// A token request may never name a repository without a policy, nor ask for more
/// than the ceiling this policy carries.
/// </summary>
public sealed class RepositoryAccessPolicy
{
    /// <summary>Binds a repository to the ceiling that governs it.</summary>
    /// <param name="repository">The allowlisted repository.</param>
    /// <param name="ceiling">The most this repository's tokens may ever request.</param>
    public RepositoryAccessPolicy(RepositoryName repository, PermissionSet ceiling)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(ceiling);

        this.Repository = repository;
        this.Ceiling = ceiling;
    }

    /// <summary>The repository a token minted under this policy is restricted to.</summary>
    public RepositoryName Repository { get; }

    /// <summary>The permission ceiling requested from GitHub, and never exceeded.</summary>
    public PermissionSet Ceiling { get; }

    /// <summary>
    /// Identity of the credential this policy produces.
    /// </summary>
    /// <remarks>
    /// Repository and effective permissions both participate, so a ceiling change
    /// cannot silently reuse a token minted under the previous, broader one.
    /// </remarks>
    public string GrantKey => $"{this.Repository.Key}|{this.Ceiling.CanonicalForm}";
}
