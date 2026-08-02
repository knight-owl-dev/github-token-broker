using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>Builds allowlist policies for tests.</summary>
internal static class TestPolicies
{
    public static RepositoryAccessPolicy Policy(
        string repository = "example-owner/example-repo",
        params (string Name, string Level)[] permissions
    )
    {
        var effective = permissions.Length == 0
            ? [("contents", "write"), ("checks", "read")]
            : permissions;

        if (!PermissionSet.TryCreate(
                effective.Select(e => new KeyValuePair<string, string>(e.Name, e.Level)),
                out var ceiling,
                out var error
            ))
        {
            throw new InvalidOperationException(error);
        }

        return new RepositoryAccessPolicy(RepositoryName.Parse(repository), ceiling);
    }

    public static RepositoryAllowlist Allowlist(params RepositoryAccessPolicy[] policies)
    {
        if (!RepositoryAllowlist.TryCreate(
                policies.Length == 0 ? [Policy()] : policies,
                out var allowlist,
                out var error
            ))
        {
            throw new InvalidOperationException(error);
        }

        return allowlist;
    }
}
