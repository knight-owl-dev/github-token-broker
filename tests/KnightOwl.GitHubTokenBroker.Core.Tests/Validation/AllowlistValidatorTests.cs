using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Core.Tests.Validation;

public sealed class AllowlistValidatorTests
{
    [Fact]
    public void AcceptsEntriesAndKeepsTheirCeilings()
    {
        Assert.True(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["example-owner/example-repo"] = Entry(("contents", "write"), ("checks", "read")),
                    ["other/upstream"] = Entry(("contents", "read")),
                },
                TestInstallations.Installation(),
                out var allowlist,
                out _
            )
        );

        Assert.Equal(2, allowlist.Count);
        Assert.True(allowlist.TryResolve(RepositoryName.Parse("example-owner/example-repo"), out var policy));
        Assert.Equal("checks:read;contents:write", policy.Ceiling.CanonicalForm);
    }

    [Fact]
    public void AnEntryTakesTheDefaultInstallationOrItsOwn()
    {
        const long expected = 345678;

        Assert.True(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["example-owner/example-repo"] = Entry(("contents", "write")),
                    ["other/upstream"] = EntryIn(expected, ("contents", "read")),
                },
                TestInstallations.Installation(),
                out var allowlist,
                out _
            )
        );

        Assert.True(allowlist.TryResolve(RepositoryName.Parse("example-owner/example-repo"), out var fallback));
        Assert.Equal(TestInstallations.DefaultNumber, fallback.Installation.Value);

        Assert.True(allowlist.TryResolve(RepositoryName.Parse("other/upstream"), out var overridden));
        Assert.Equal(expected, overridden.Installation.Value);

        Assert.Equal(2, allowlist.InstallationCount);
    }

    [Fact]
    public void CountsOneInstallationWhenNoEntryOverridesIt()
    {
        // Paired with the case above, which starts from two: a count that never
        // moves would satisfy either test alone.
        Assert.True(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["example-owner/example-repo"] = Entry(("contents", "write")),
                    ["other/upstream"] = Entry(("contents", "read")),
                },
                TestInstallations.Installation(),
                out var allowlist,
                out _
            )
        );

        Assert.Equal(1, allowlist.InstallationCount);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void RefusesANonPositiveInstallationOverride(long installationId)
    {
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["owner/repo"] = EntryIn(installationId, ("contents", "read")),
                },
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.Equal(
            "The repositories entry \"owner/repo\" installation_id must be a positive number.",
            error
        );
    }

    [Fact]
    public void RefusesAnAbsentAllowlist()
    {
        Assert.False(AllowlistValidator.TryValidate(null, TestInstallations.Installation(), out _, out var error));

        Assert.Equal("The repositories section must declare at least one repository.", error);
    }

    [Fact]
    public void RefusesAnEmptyAllowlist()
    {
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal),
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.Equal("The repositories section must declare at least one repository.", error);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("owner/repo.git")]
    public void RefusesAMalformedKey(string key)
    {
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    [key] = Entry(("contents", "read")),
                },
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.StartsWith($"The repositories key \"{key}\" is invalid: ", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnEntryWithNoPermissions()
    {
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["owner/repo"] = new(),
                    ["other/repo"] = Entry(),
                },
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.Equal("The repositories entry \"owner/repo\" must declare permissions.", error);
    }

    /// <param name="name">The permission name.</param>
    /// <param name="level">The level requested for it.</param>
    /// <remarks>
    /// <c>workflows</c> is refused at any level rather than only at an
    /// unsupported one, since this broker never requests it.
    /// </remarks>
    [Theory]
    [InlineData("workflows", "write")]
    [InlineData("not-a-permission", "read")]
    [InlineData("contents", "sideways")]
    public void RefusesAnInvalidPermission(string name, string level)
    {
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["owner/repo"] = Entry((name, level)),
                },
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.StartsWith(
            "The repositories entry \"owner/repo\" has invalid permissions: ",
            error,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RefusesTwoKeysDifferingOnlyByCase()
    {
        // Both spellings resolve to one identity, so accepting them would leave two
        // ceilings competing for the same repository.
        Assert.False(
            AllowlistValidator.TryValidate(
                new Dictionary<string, RepositoryDocument>(StringComparer.Ordinal)
                {
                    ["Owner/Repo"] = Entry(("contents", "write")),
                    ["owner/repo"] = Entry(("contents", "read")),
                },
                TestInstallations.Installation(),
                out _,
                out var error
            )
        );

        Assert.StartsWith("The repositories section is invalid: ", error, StringComparison.Ordinal);
        Assert.Contains("more than once", error, StringComparison.Ordinal);
    }

    private static RepositoryDocument Entry(params (string Name, string Level)[] permissions)
        => new()
        {
            Permissions = PermissionsOf(permissions),
        };

    private static RepositoryDocument EntryIn(
        long installationId,
        params (string Name, string Level)[] permissions
    )
        => new()
        {
            InstallationId = installationId,
            Permissions = PermissionsOf(permissions),
        };

    private static Dictionary<string, string> PermissionsOf((string Name, string Level)[] permissions)
        => permissions.ToDictionary(
            permission => permission.Name,
            permission => permission.Level,
            StringComparer.Ordinal
        );
}
