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
    public void ReadsAUnixSocketEndpointWithoutACredential()
    {
        Assert.True(
            ClientOptions.TryRead(
                Environment((ClientOptions.EndpointVariable, "unix:///run/broker.sock")),
                out var options,
                out _
            )
        );

        Assert.Equal(BrokerEndpointKind.UnixSocket, options.Endpoint.Kind);
        Assert.Null(options.ClientCredential);
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
    public void RequiresACredentialForAnHttpEndpoint()
    {
        // Plain TCP is reachable by anything that can route to the port, so the broker
        // will refuse a request without the shared secret.
        Assert.False(
            ClientOptions.TryRead(
                Environment((ClientOptions.EndpointVariable, "http://host.docker.internal:8765")),
                out _,
                out var error
            )
        );

        Assert.Contains(ClientOptions.CredentialFileVariable, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheCredentialFromAFileAndStripsItsNewline()
    {
        var path = Path.Combine(_root, "credential");
        File.WriteAllText(path, "a-high-entropy-value\n");

        Assert.True(
            ClientOptions.TryRead(
                Environment(
                    (ClientOptions.EndpointVariable, "http://host:8765"),
                    (ClientOptions.CredentialFileVariable, path)
                ),
                out var options,
                out _
            )
        );

        Assert.Equal("a-high-entropy-value", options.ClientCredential);
    }

    [Fact]
    public void PrefersTheCredentialFileOverTheInlineValue()
    {
        // A file can be restricted by ownership and mode; an environment variable is
        // visible to anything that can read the process environment.
        var path = Path.Combine(_root, "credential");
        File.WriteAllText(path, "from-file");

        Assert.True(
            ClientOptions.TryRead(
                Environment(
                    (ClientOptions.EndpointVariable, "http://host:8765"),
                    (ClientOptions.CredentialFileVariable, path),
                    (ClientOptions.CredentialVariable, "from-environment")
                ),
                out var options,
                out _
            )
        );

        Assert.Equal("from-file", options.ClientCredential);
    }

    [Fact]
    public void AcceptsAnInlineCredentialWhenNoFileIsGiven()
    {
        Assert.True(
            ClientOptions.TryRead(
                Environment(
                    (ClientOptions.EndpointVariable, "http://host:8765"),
                    (ClientOptions.CredentialVariable, "from-environment")
                ),
                out var options,
                out _
            )
        );

        Assert.Equal("from-environment", options.ClientCredential);
    }

    [Fact]
    public void ReportsAnUnreadableCredentialFile()
    {
        Assert.False(
            ClientOptions.TryRead(
                Environment(
                    (ClientOptions.EndpointVariable, "http://host:8765"),
                    (ClientOptions.CredentialFileVariable, Path.Combine(_root, "absent"))
                ),
                out _,
                out var error
            )
        );

        Assert.Contains(ClientOptions.CredentialFileVariable, error, StringComparison.Ordinal);
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
