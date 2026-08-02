using KnightOwl.GitHubTokenBroker.Cli.Infrastructure;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

public sealed class ClientOptionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hga-options").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private static Func<string, string?> Environment(params (string Name, string? Value)[] entries)
    {
        var map = entries.ToDictionary(
            entry => entry.Name,
            entry => entry.Value,
            StringComparer.Ordinal
        );

        return map.GetValueOrDefault;
    }

    [Fact]
    public void ReadsAUnixSocketEndpoint()
    {
        Assert.True(
            ClientOptions.TryRead(
                Environment((ClientOptions.EndpointVariable, "unix:///run/broker.sock")),
                out var options,
                out _
            )
        );

        Assert.Equal("/run/broker.sock", options.Endpoint.UnixSocketPath);
        Assert.Null(options.GitHubCliPath);
    }

    [Fact]
    public void RequiresAnEndpoint()
    {
        Assert.False(ClientOptions.TryRead(Environment(), out _, out var error));
        Assert.Contains(ClientOptions.EndpointVariable, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("broker.sock")]
    [InlineData("tcp://host:1")]
    [InlineData("http://127.0.0.1:8765")]
    [InlineData("https://broker.example.com")]
    public void RejectsAnUnusableEndpoint(string endpoint)
    {
        Assert.False(
            ClientOptions.TryRead(
                Environment((ClientOptions.EndpointVariable, endpoint)),
                out _,
                out var error
            )
        );

        Assert.Contains(ClientOptions.EndpointVariable, error, StringComparison.Ordinal);
    }

    [Fact]
    public void CarriesAnExplicitGitHubCliPath()
    {
        Assert.True(
            ClientOptions.TryRead(
                Environment(
                    (ClientOptions.EndpointVariable, "unix:///run/broker.sock"),
                    (ClientOptions.GitHubCliVariable, "/usr/local/bin/gh")
                ),
                out var options,
                out _
            )
        );

        Assert.Equal("/usr/local/bin/gh", options.GitHubCliPath);
    }
}
