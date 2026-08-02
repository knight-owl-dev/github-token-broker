using System.Diagnostics.CodeAnalysis;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>Validates <c>repositories</c>.</summary>
internal static class AllowlistValidator
{
    /// <summary>Turns the configured entries into the allowlist the broker enforces.</summary>
    /// <param name="configured">The configured entries, keyed by repository.</param>
    /// <param name="allowlist">The allowlist, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when every entry is usable.</returns>
    internal static bool TryValidate(
        Dictionary<string, RepositoryDocument>? configured,
        [NotNullWhen(true)] out RepositoryAllowlist? allowlist,
        [NotNullWhen(false)] out string? error
    )
    {
        allowlist = null;

        if (configured is null || configured.Count == 0)
        {
            error = "The repositories section must declare at least one repository.";
            return false;
        }

        List<RepositoryAccessPolicy> policies = new(configured.Count);

        foreach ((var key, var entry) in configured)
        {
            if (!RepositoryName.TryParse(key, out var repository, out var repositoryError))
            {
                error = $"The repositories key \"{key}\" is invalid: {repositoryError}.";
                return false;
            }

            if (entry?.Permissions is null || entry.Permissions.Count == 0)
            {
                error = $"The repositories entry \"{key}\" must declare permissions.";
                return false;
            }

            if (!PermissionSet.TryCreate(entry.Permissions, out var ceiling, out var permissionError))
            {
                error = $"The repositories entry \"{key}\" has invalid permissions: {permissionError}.";
                return false;
            }

            policies.Add(new RepositoryAccessPolicy(repository, ceiling));
        }

        if (!RepositoryAllowlist.TryCreate(policies, out allowlist, out var allowlistError))
        {
            error = $"The repositories section is invalid: {allowlistError}.";
            return false;
        }

        error = null;
        return true;
    }
}
