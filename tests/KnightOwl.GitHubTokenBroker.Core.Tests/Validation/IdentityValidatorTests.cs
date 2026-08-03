using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Core.Tests.Validation;

public sealed class IdentityValidatorTests
{
    [Fact]
    public void AcceptsAMinimalIdentity()
    {
        Assert.True(IdentityValidator.TryValidate(Document(), out var identity, out _));

        Assert.Equal(GitHubHost.GitHubCom, identity.Host);
        Assert.Equal(123456, identity.AppId);
        Assert.Equal(789012, identity.Installation.Value);
        Assert.Equal("/keys/app.pem", identity.PrivateKeyPath);
    }

    [Fact]
    public void DefaultsTheHostWhenUnconfigured()
    {
        Assert.True(IdentityValidator.TryValidate(Document(host: null), out var identity, out _));
        Assert.Equal(GitHubHost.GitHubCom, identity.Host);
    }

    [Fact]
    public void RefusesAnUnsupportedHost()
    {
        Assert.False(
            IdentityValidator.TryValidate(
                Document(host: "github.example.com"),
                out _,
                out var error
            )
        );

        Assert.StartsWith("The github_host value is invalid: ", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "The app_id value is required.")]
    [InlineData(0L, "The app_id value must be a positive number.")]
    [InlineData(-1L, "The app_id value must be a positive number.")]
    public void RefusesABadAppId(long? appId, string expected)
    {
        Assert.False(IdentityValidator.TryValidate(Document(appId: appId), out _, out var error));

        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(null, "The installation_id value is required.")]
    [InlineData(0L, "The installation_id value must be a positive number.")]
    [InlineData(-1L, "The installation_id value must be a positive number.")]
    public void RefusesABadInstallationId(long? installationId, string expected)
    {
        Assert.False(
            IdentityValidator.TryValidate(
                Document(installationId: installationId),
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData(null, "The private_key_path value is required.")]
    [InlineData("app.pem", "The private_key_path value must be absolute.")]
    [InlineData("keys/app.pem", "The private_key_path value must be absolute.")]
    public void RefusesABadPrivateKeyPath(string? path, string expected)
    {
        Assert.False(
            IdentityValidator.TryValidate(
                Document(privateKeyPath: path),
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    [Fact]
    public void ReportsTheHostBeforeTheIdentifiers()
    {
        // Precedence is fixed so an operator fixing one fault at a time sees a
        // stable order rather than a shuffling diagnostic.
        Assert.False(
            IdentityValidator.TryValidate(
                Document(host: "github.example.com", appId: 0),
                out _,
                out var error
            )
        );

        Assert.StartsWith("The github_host", error, StringComparison.Ordinal);
    }

    private static ConfigurationDocument Document(
        string? host = GitHubHost.GitHubComName,
        long? appId = 123456,
        long? installationId = 789012,
        string? privateKeyPath = "/keys/app.pem"
    )
        => new()
        {
            GithubHost = host,
            AppId = appId,
            InstallationId = installationId,
            PrivateKeyPath = privateKeyPath,
        };
}
