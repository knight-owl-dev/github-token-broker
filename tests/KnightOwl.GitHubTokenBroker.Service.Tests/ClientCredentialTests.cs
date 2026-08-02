using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class ClientCredentialTests : IDisposable
{
    private const string Secret = "0123456789abcdef0123456789abcdef";

    private readonly string _root = Directory.CreateTempSubdirectory("hga-cred").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Write(string contents)
    {
        var path = Path.Combine(_root, "credential");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void MatchesTheConfiguredSecret()
    {
        var credential = ClientCredential.Load(Write(Secret));

        Assert.True(credential.Matches(Secret));
    }

    [Fact]
    public void IgnoresATrailingNewline()
    {
        // Every editor and `openssl rand` pipeline leaves one behind.
        var credential = ClientCredential.Load(Write(Secret + "\n"));

        Assert.True(credential.Matches(Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdeff")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    public void RefusesAnythingElse(string? presented)
    {
        var credential = ClientCredential.Load(Write(Secret));

        Assert.False(credential.Matches(presented));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcde")]
    public void RefusesToLoadTooLittleEntropy(string contents)
    {
        var failure = Assert.Throws<ConfigurationException>(() => ClientCredential.Load(Write(contents)));

        Assert.Contains("at least", failure.Message, StringComparison.Ordinal);

        // The message must not echo the value it rejected.
        if (contents.Length > 0)
        {
            Assert.DoesNotContain(contents, failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReportsAMissingFileByPath()
    {
        var path = Path.Combine(_root, "absent");

        var failure = Assert.Throws<ConfigurationException>(() => ClientCredential.Load(path));

        Assert.Contains(path, failure.Message, StringComparison.Ordinal);
    }
}
