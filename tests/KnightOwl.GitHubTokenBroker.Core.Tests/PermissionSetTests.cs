using KnightOwl.GitHubTokenBroker.Domain.Permissions;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class PermissionSetTests
{
    private static PermissionSet Create(params (string Name, string Level)[] entries)
    {
        Assert.True(
            PermissionSet.TryCreate(
                entries.Select(e => new KeyValuePair<string, string>(e.Name, e.Level)),
                out var set,
                out var error
            ),
            error
        );

        return set;
    }

    [Fact]
    public void CanonicalFormIsOrderIndependent()
    {
        var one = Create(("contents", "write"), ("actions", "read"));
        var other = Create(("actions", "read"), ("contents", "write"));

        Assert.Equal("actions:read;contents:write", one.CanonicalForm);
        Assert.Equal(one, other);
    }

    [Fact]
    public void RefusesWorkflowsAtEveryLevel()
    {
        foreach (var level in new[] { "read", "write" })
        {
            Assert.False(
                PermissionSet.TryCreate(
                    [new KeyValuePair<string, string>("workflows", level)],
                    out _,
                    out var error
                )
            );

            Assert.Contains("never requested", error, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("administration", "write")]
    [InlineData("secrets", "read")]
    [InlineData("contents", "admin")]
    [InlineData("contents", "")]
    [InlineData("contents", "WRITE")]
    public void RefusesUnsupportedNamesAndLevels(string name, string level)
        => Assert.False(
            PermissionSet.TryCreate(
                [new KeyValuePair<string, string>(name, level)],
                out _,
                out _
            )
        );

    [Fact]
    public void RefusesAnEmptyMap()
        => Assert.False(PermissionSet.TryCreate([], out _, out _));

    [Fact]
    public void RefusesADuplicateName()
        => Assert.False(
            PermissionSet.TryCreate(
                [new KeyValuePair<string, string>("contents", "read"), new KeyValuePair<string, string>("contents", "write"),],
                out _,
                out _
            )
        );

    [Fact]
    public void CoversAcceptsEqualOrLowerLevels()
    {
        var ceiling = Create(("contents", "write"), ("checks", "read"));

        Assert.True(
            ceiling.Covers(
                [
                    new KeyValuePair<string, PermissionLevel>("contents", PermissionLevel.Read),
                    new KeyValuePair<string, PermissionLevel>("checks", PermissionLevel.Read),
                ],
                out _
            )
        );
    }

    [Fact]
    public void CoversRejectsAHigherLevel()
    {
        var ceiling = Create(("checks", "read"));

        Assert.False(
            ceiling.Covers(
                [new KeyValuePair<string, PermissionLevel>("checks", PermissionLevel.Write)],
                out var excess
            )
        );

        Assert.Contains("above configured", excess, StringComparison.Ordinal);
    }

    [Fact]
    public void CoversRejectsAnUnconfiguredPermission()
    {
        var ceiling = Create(("contents", "write"));

        Assert.False(
            ceiling.Covers(
                [new KeyValuePair<string, PermissionLevel>("issues", PermissionLevel.Write)],
                out var excess
            )
        );

        Assert.Contains("unconfigured", excess, StringComparison.Ordinal);
    }

    [Fact]
    public void CoversToleratesImplicitMetadataRead()
    {
        // GitHub reports metadata read on every installation token whether it
        // was requested, so tolerating it is required rather than lenient.
        var ceiling = Create(("contents", "write"));

        Assert.True(
            ceiling.Covers(
                [new KeyValuePair<string, PermissionLevel>("metadata", PermissionLevel.Read)],
                out _
            )
        );

        Assert.False(
            ceiling.Covers(
                [new KeyValuePair<string, PermissionLevel>("metadata", PermissionLevel.Write)],
                out _
            )
        );
    }

    [Fact]
    public void WireMapUsesGitHubLevelNames()
    {
        var wire = Create(("contents", "write"), ("checks", "read"))
            .ToWireMap();

        Assert.Equal("write", wire["contents"]);
        Assert.Equal("read", wire["checks"]);
    }
}
