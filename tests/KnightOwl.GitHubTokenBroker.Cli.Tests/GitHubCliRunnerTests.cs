using KnightOwl.GitHubTokenBroker.Cli.Application;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

public sealed class GitHubCliRunnerTests : IDisposable
{
    private const string Token = "ghs_opaqueTokenValue";

    private readonly string _root = Directory.CreateTempSubdirectory("hga-gh").FullName;
    private readonly string _ghPath;

    public GitHubCliRunnerTests()
    {
        _ghPath = Path.Combine(_root, "gh");
        File.WriteAllText(_ghPath, "#!/bin/sh\nexit 0\n");
    }

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private GitHubCliRunner Runner(
        StubBrokerClient broker,
        RecordingProcessLauncher launcher,
        TextWriter error,
        string? configuredPath = null,
        string? ownExecutable = null
    )
        => new(
            broker,
            launcher,
            name => name == "PATH" ? _root : null,
            configuredPath ?? _ghPath,
            ownExecutable,
            error
        );

    [Fact]
    public async Task PutsTheTokenOnlyInTheChildEnvironment()
    {
        var broker = StubBrokerClient.Returning(Token);
        RecordingProcessLauncher launcher = new();
        await using StringWriter error = new();

        var exitCode = await Runner(broker, launcher, error).RunAsync(
            RepositoryName.Parse("example-owner/example-repo"),
            ["pr", "list"],
            CancellationToken.None
        );

        Assert.Equal(0, exitCode);
        Assert.True(launcher.WasInvoked);
        Assert.Equal(Token, launcher.Environment["GH_TOKEN"]);

        // An inherited token must not be able to take precedence.
        Assert.Null(launcher.Environment["GITHUB_TOKEN"]);
        Assert.True(launcher.Environment.ContainsKey("GITHUB_TOKEN"));

        // The child is pinned to the repository the token is scoped to.
        Assert.Equal("example-owner/example-repo", launcher.Environment["GH_REPO"]);
    }

    [Fact]
    public async Task PassesArgumentsThroughUntouched()
    {
        var broker = StubBrokerClient.Returning(Token);
        RecordingProcessLauncher launcher = new();
        await using StringWriter error = new();

        string[] arguments = ["pr", "create", "--title", "a title with spaces", "--body", "$(command substitution) && rm -rf /",];

        await Runner(broker, launcher, error).RunAsync(
            RepositoryName.Parse("example-owner/example-repo"),
            arguments,
            CancellationToken.None
        );

        // Nothing is quoted, joined, or reinterpreted: the list arrives as given.
        Assert.Equal(arguments, launcher.Arguments);
        Assert.Equal(_ghPath, launcher.ExecutablePath);
    }

    [Fact]
    public async Task NoArgumentCarriesTheToken()
    {
        var broker = StubBrokerClient.Returning(Token);
        RecordingProcessLauncher launcher = new();
        await using StringWriter error = new();

        await Runner(broker, launcher, error).RunAsync(
            RepositoryName.Parse("example-owner/example-repo"),
            ["auth", "status"],
            CancellationToken.None
        );

        Assert.DoesNotContain(Token, launcher.Arguments);
        Assert.DoesNotContain(Token, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(127)]
    public async Task PreservesTheChildExitStatus(int childExitCode)
    {
        var broker = StubBrokerClient.Returning(Token);
        RecordingProcessLauncher launcher = new(childExitCode);
        await using StringWriter error = new();

        var exitCode = await Runner(broker, launcher, error).RunAsync(
            RepositoryName.Parse("example-owner/example-repo"),
            ["pr", "list"],
            CancellationToken.None
        );

        Assert.Equal(childExitCode, exitCode);
    }

    [Theory]
    [InlineData(BrokerClientFailure.Refused, CliExitCode.NotAuthorized)]
    [InlineData(BrokerClientFailure.Unavailable, CliExitCode.Unavailable)]
    [InlineData(BrokerClientFailure.Unauthenticated, CliExitCode.Internal)]
    [InlineData(BrokerClientFailure.Failed, CliExitCode.Internal)]
    public async Task NeverStartsTheChildWithoutAToken(BrokerClientFailure failure, int expectedExitCode)
    {
        var broker = StubBrokerClient.Failing(failure);
        RecordingProcessLauncher launcher = new();
        await using StringWriter error = new();

        var exitCode = await Runner(broker, launcher, error).RunAsync(
            RepositoryName.Parse("example-owner/example-repo"),
            ["pr", "list"],
            CancellationToken.None
        );

        Assert.Equal(expectedExitCode, exitCode);
        Assert.False(launcher.WasInvoked);
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public async Task FailsBeforeMintingWhenTheGitHubCliIsMissing()
    {
        var broker = StubBrokerClient.Returning(Token);
        RecordingProcessLauncher launcher = new();
        await using StringWriter error = new();

        var exitCode = await Runner(
                broker,
                launcher,
                error,
                configuredPath: Path.Combine(_root, "no-such-gh")
            )
            .RunAsync(
                RepositoryName.Parse("example-owner/example-repo"),
                ["pr", "list"],
                CancellationToken.None
            );

        Assert.Equal(CliExitCode.Usage, exitCode);
        Assert.False(launcher.WasInvoked);

        // No token is requested if the child could never run anyway.
        Assert.Empty(broker.TokenRequests);
    }
}
