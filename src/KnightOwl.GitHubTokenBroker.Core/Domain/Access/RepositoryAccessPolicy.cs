using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Domain.Access;

/// <summary>
/// One allowlisted repository, the installation it is minted against, and the
/// permission ceiling the operator granted it. A token request may never name a
/// repository without a policy, nor ask for more than the ceiling this policy
/// carries.
/// </summary>
public sealed class RepositoryAccessPolicy
{
    /// <summary>Binds a repository to the installation and ceiling that govern it.</summary>
    /// <param name="repository">The allowlisted repository.</param>
    /// <param name="installation">The installation this repository is minted against.</param>
    /// <param name="ceiling">The most this repository's tokens may ever request.</param>
    public RepositoryAccessPolicy(
        RepositoryName repository,
        InstallationId installation,
        PermissionSet ceiling
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(ceiling);

        this.Repository = repository;
        this.Installation = installation;
        this.Ceiling = ceiling;
    }

    /// <summary>The repository a token minted under this policy is restricted to.</summary>
    public RepositoryName Repository { get; }

    /// <summary>The installation whose tokens serve this repository.</summary>
    public InstallationId Installation { get; }

    /// <summary>The permission ceiling requested from GitHub, and never exceeded.</summary>
    public PermissionSet Ceiling { get; }

    /// <summary>
    /// Identity of the credential this policy produces.
    /// </summary>
    /// <remarks>
    /// Repository, installation, and effective permissions all participate, so a
    /// configuration change cannot silently reuse a token minted under the
    /// previous, broader grant.
    /// </remarks>
    public string GrantKey
        => $"{this.Repository.Key}|{this.Installation.Text}|{this.Ceiling.CanonicalForm}";
}
