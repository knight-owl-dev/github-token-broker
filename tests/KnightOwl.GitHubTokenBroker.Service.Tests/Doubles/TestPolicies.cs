using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>Builds allowlist policies for tests.</summary>
internal static class TestPolicies
{
    public const long DefaultInstallation = 789012;

    public static RepositoryAccessPolicy Policy(
        string repository = "example-owner/example-repo",
        params (string Name, string Level)[] permissions
    )
        => PolicyIn(DefaultInstallation, repository, permissions);

    public static RepositoryAccessPolicy PolicyIn(
        long installationId,
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

        if (!InstallationId.TryCreate(installationId, out var installation, out var installationError))
        {
            throw new InvalidOperationException(installationError);
        }

        return new RepositoryAccessPolicy(RepositoryName.Parse(repository), installation, ceiling);
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
