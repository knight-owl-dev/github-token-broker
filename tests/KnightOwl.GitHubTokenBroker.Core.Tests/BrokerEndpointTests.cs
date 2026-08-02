using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class BrokerEndpointTests
{
    [Fact]
    public void ParsesAUnixSocketEndpoint()
    {
        var endpoint = BrokerEndpoint.Parse("unix:///run/github-token-broker/broker.sock");

        Assert.Equal("/run/github-token-broker/broker.sock", endpoint.UnixSocketPath);

        // The authority is a placeholder: the socket path decides the peer.
        Assert.Equal("http://localhost/", endpoint.BaseUri.AbsoluteUri);
    }

    /// <param name="value">An endpoint as a client might set it.</param>
    /// <remarks>
    /// Beyond the malformed and unsupported schemes: a relative socket path, a
    /// host where a socket path allows none, and the query and fragment no
    /// endpoint may carry.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("broker.sock")]
    [InlineData("/run/broker.sock")]
    [InlineData("tcp://127.0.0.1:8765")]
    [InlineData("ftp://host")]
    [InlineData("unix://broker.sock")]
    [InlineData("unix://host/run/broker.sock")]
    [InlineData("unix:///run/broker.sock?x=1")]
    [InlineData("unix:///run/broker.sock#f")]
    public void RejectsUnusableEndpoints(string value)
    {
        Assert.False(BrokerEndpoint.TryParse(value, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.NotNull(error);
    }

    /// <param name="value">An endpoint naming a transport the broker no longer serves.</param>
    /// <remarks>
    /// Both read as plausible, so each is refused by name rather than as an
    /// unrecognized scheme.
    /// </remarks>
    [Theory]
    [InlineData("http://127.0.0.1:8765")]
    [InlineData("https://broker.example.com")]
    public void RejectsHttpBySayingWhatIsServed(string value)
    {
        Assert.False(BrokerEndpoint.TryParse(value, out _, out var error));

        Assert.Contains("Unix socket", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNull()
        => Assert.False(BrokerEndpoint.TryParse(null, out _, out _));

    [Fact]
    public void ParseThrowsOnAnUnsupportedScheme()
        => Assert.Throws<FormatException>(() => BrokerEndpoint.Parse("tcp://127.0.0.1:1"));

    [Fact]
    public void RoundTripsThroughToString()
        => Assert.Equal(
            "unix:///run/broker.sock",
            BrokerEndpoint.Parse("unix:///run/broker.sock").ToString()
        );
}
