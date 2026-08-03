using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Domain.Access;

/// <summary>
/// The set of repositories this broker will mint for. Authorization is a lookup
/// against operator-owned policy, so a repository the operator did not list is
/// indistinguishable from one that does not exist.
/// </summary>
public sealed class RepositoryAllowlist
{
    private readonly FrozenDictionary<string, RepositoryAccessPolicy> _policies;

    private RepositoryAllowlist(FrozenDictionary<string, RepositoryAccessPolicy> policies)
    {
        _policies = policies;
        this.InstallationCount = policies.Values.Select(policy => policy.Installation).Distinct().Count();
    }

    /// <summary>How many repositories are allowlisted.</summary>
    public int Count => _policies.Count;

    /// <summary>How many distinct installations those repositories are spread over.</summary>
    public int InstallationCount { get; }

    /// <summary>Every policy in the allowlist, in no guaranteed order.</summary>
    public IReadOnlyCollection<RepositoryAccessPolicy> Policies => _policies.Values;

    /// <summary>Builds an allowlist from validated policies.</summary>
    /// <param name="policies">Candidate policies, one per repository.</param>
    /// <param name="allowlist">The allowlist when creation succeeds.</param>
    /// <param name="error">Why creation was refused, when it fails.</param>
    /// <returns><see langword="true"/> when the policies form a usable allowlist.</returns>
    /// <remarks>
    /// An empty set is refused, as is a pair of entries differing only by case,
    /// which would otherwise resolve to one identity carrying two ceilings.
    /// </remarks>
    public static bool TryCreate(
        IEnumerable<RepositoryAccessPolicy> policies,
        [NotNullWhen(true)] out RepositoryAllowlist? allowlist,
        [NotNullWhen(false)] out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(policies);

        allowlist = null;
        Dictionary<string, RepositoryAccessPolicy> indexed = new(StringComparer.Ordinal);

        foreach (var policy in policies)
        {
            if (indexed.TryAdd(policy.Repository.Key, policy))
            {
                continue;
            }

            error = $"repository \"{policy.Repository.Key}\" is listed more than once, ignoring case";

            return false;
        }

        if (indexed.Count == 0)
        {
            error = "the allowlist must contain at least one repository";
            return false;
        }

        allowlist = new RepositoryAllowlist(indexed.ToFrozenDictionary(StringComparer.Ordinal));
        error = null;
        return true;
    }

    /// <summary>Finds the policy governing a repository.</summary>
    /// <param name="repository">The requested repository.</param>
    /// <param name="policy">The governing policy when the repository is allowlisted.</param>
    /// <returns><see langword="true"/> when the repository is allowlisted.</returns>
    public bool TryResolve(
        RepositoryName repository,
        [NotNullWhen(true)] out RepositoryAccessPolicy? policy
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        return _policies.TryGetValue(repository.Key, out policy);
    }
}
