using KnightOwl.GitHubTokenBroker.Cli.Application;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

public sealed class CredentialHelperTests
{
    private const string Token = "ghs_opaqueTokenValue";

    private const string AllowlistedRequest =
        "protocol=https\nhost=github.com\npath=example-owner/example-repo.git\n\n";

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        StubBrokerClient broker,
        string input,
        string? operation = "get"
    )
    {
        await using StringWriter output = new();
        await using StringWriter error = new();
        using StringReader reader = new(input);

        var exitCode = await new CredentialHelper(broker, reader, output, error)
            .RunAsync(operation, CancellationToken.None);

        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task WritesTheCredentialGitExpects()
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, error) = await RunAsync(broker, AllowlistedRequest);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal($"username=x-access-token\npassword={Token}\n\n", output);
        Assert.Empty(error);
        Assert.Equal("example-owner/example-repo", Assert.Single(broker.TokenRequests));
    }

    [Fact]
    public async Task AcceptsAPathWithoutTheGitSuffix()
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, _) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\npath=example-owner/example-repo\n\n"
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains($"password={Token}", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("erase")]
    [InlineData("unknown-operation")]
    [InlineData(null)]
    public async Task DoesNotMintForAnyOperationOtherThanGet(string? operation)
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, error) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\npath=example-owner/example-repo.git\npassword=secret\n\n",
            operation
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
        Assert.Empty(broker.TokenRequests);
    }

    [Fact]
    public async Task DeclinesQuietlyForARepositoryTheBrokerRefuses()
    {
        // Git reads no credential and proceeds unauthenticated, which is what lets a
        // public upstream keep working alongside an allowlisted fork.
        var broker = StubBrokerClient.Failing(BrokerClientFailure.Refused);

        var (exitCode, output, error) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\npath=other/upstream.git\n\n"
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    /// <param name="input">A credential request written by Git.</param>
    /// <remarks>
    /// The three kinds a helper must stay quiet about: another host, another
    /// protocol, and a request naming nothing at all. Git asks broadly, so
    /// answering only what this serves keeps unrelated fetches quiet.
    /// </remarks>
    [Theory]
    [InlineData("protocol=https\nhost=gitlab.com\npath=owner/repo.git\n\n")]
    [InlineData("protocol=http\nhost=github.com\npath=owner/repo.git\n\n")]
    [InlineData("protocol=ssh\nhost=github.com\npath=owner/repo.git\n\n")]
    [InlineData("\n")]
    [InlineData("")]
    public async Task DeclinesSilentlyForRequestsItDoesNotServe(string input)
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, error) = await RunAsync(broker, input);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
        Assert.Empty(broker.TokenRequests);
    }

    [Fact]
    public async Task ReportsAMissingPathBecauseThatIsMisconfiguration()
    {
        // Without useHttpPath, Git would reuse one repository's token for another.
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, error) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\n\n"
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(output);
        Assert.Empty(broker.TokenRequests);

        // A request for another host is silent, but a github.com request that cannot
        // name a repository points at broken Git configuration.
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task ReportsAnUnparsablePath()
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, error) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\npath=owner/repo/extra\n\n"
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(output);
        Assert.NotEmpty(error);
        Assert.Empty(broker.TokenRequests);
    }

    [Fact]
    public async Task FailsLoudlyWhenTheBrokerIsUnreachable()
    {
        // Degrading to anonymous here would turn a broker outage into a confusing
        // authentication failure against a private repository.
        var broker = StubBrokerClient.Failing(BrokerClientFailure.Unavailable);

        var (exitCode, output, error) = await RunAsync(broker, AllowlistedRequest);

        Assert.Equal(CliExitCode.Unavailable, exitCode);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData(BrokerClientFailure.Unauthenticated)]
    [InlineData(BrokerClientFailure.Failed)]
    public async Task FailsLoudlyForOtherBrokerFailures(BrokerClientFailure failure)
    {
        var broker = StubBrokerClient.Failing(failure);

        var (exitCode, output, error) = await RunAsync(broker, AllowlistedRequest);

        Assert.Equal(CliExitCode.Internal, exitCode);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task RejectsAnOversizedDescription()
    {
        var broker = StubBrokerClient.Returning(Token);
        var flood = string.Concat(Enumerable.Repeat("junk=x\n", 200));

        var (exitCode, output, _) = await RunAsync(broker, flood);

        Assert.Equal(CliExitCode.Usage, exitCode);
        Assert.Empty(output);
        Assert.Empty(broker.TokenRequests);
    }

    [Fact]
    public async Task IgnoresUnrelatedFieldsIncludingAnIncomingPassword()
    {
        var broker = StubBrokerClient.Returning(Token);

        var (exitCode, output, _) = await RunAsync(
            broker,
            "protocol=https\nhost=github.com\npath=example-owner/example-repo.git\n"
            + "username=someone\npassword=an-old-secret\nwwwauth[]=Basic\n\n"
        );

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains($"password={Token}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("an-old-secret", output, StringComparison.Ordinal);
    }
}
