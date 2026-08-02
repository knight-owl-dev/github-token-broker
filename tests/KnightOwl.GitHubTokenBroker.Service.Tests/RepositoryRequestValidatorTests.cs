using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class RepositoryRequestValidatorTests
{
    private static RepositoryRequestValidator Validator()
        => new(GitHubHost.GitHubCom, TestPolicies.Allowlist());

    [Fact]
    public void ResolvesAnAllowlistedRepository()
    {
        Assert.True(
            Validator().TryResolve(
                "github.com",
                "example-owner/example-repo",
                out var policy,
                out _
            )
        );

        Assert.Equal("example-owner/example-repo", policy.Repository.FullName);
    }

    [Fact]
    public void ResolvesRegardlessOfRequestCasing()
        => Assert.True(Validator().TryResolve("github.com", "EXAMPLE-OWNER/EXAMPLE-REPO", out _, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("github.example.com")]
    [InlineData("GITHUB.COM")]
    [InlineData("gitlab.com")]
    public void RefusesAnUnsupportedHost(string? host)
    {
        Assert.False(Validator().TryResolve(host, "example-owner/example-repo", out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("personal-account/not-listed")]
    [InlineData("someone-else/example-repo")]
    [InlineData("https://github.com/example-owner/example-repo")]
    [InlineData("example-owner/example-repo/extra")]
    [InlineData("../../etc/passwd")]
    public void RefusesAnythingNotAllowlisted(string? repository)
    {
        Assert.False(Validator().TryResolve("github.com", repository, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ReportsTheCeilingItResolved()
    {
        RepositoryRequestValidator validator = new(
            GitHubHost.GitHubCom,
            TestPolicies.Allowlist(TestPolicies.Policy("owner/repo", ("contents", "read")))
        );

        Assert.True(validator.TryResolve("github.com", "owner/repo", out var policy, out _));
        Assert.Equal("contents:read", policy.Ceiling.CanonicalForm);
    }
}
