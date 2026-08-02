using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class BrokerEndpointTests
{
    [Fact]
    public void ParsesAUnixSocketEndpoint()
    {
        var endpoint = BrokerEndpoint.Parse("unix:///run/github-token-broker/broker.sock");

        Assert.Equal(BrokerEndpointKind.UnixSocket, endpoint.Kind);
        Assert.Equal("/run/github-token-broker/broker.sock", endpoint.UnixSocketPath);

        // The authority is a placeholder: the socket path decides the peer.
        Assert.Equal("http://localhost/", endpoint.BaseUri.AbsoluteUri);
    }

    [Fact]
    public void ParsesAnHttpEndpoint()
    {
        var endpoint = BrokerEndpoint.Parse("http://host.docker.internal:8765");

        Assert.Equal(BrokerEndpointKind.Http, endpoint.Kind);
        Assert.Null(endpoint.UnixSocketPath);
        Assert.Equal("http://host.docker.internal:8765/", endpoint.BaseUri.AbsoluteUri);
    }

    /// <param name="value">An endpoint as a client might set it.</param>
    /// <remarks>
    /// Beyond the malformed and unsupported schemes: a relative socket path, a
    /// host where a socket path allows none, and the path, query, and fragment
    /// an HTTP endpoint must not carry.
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
    [InlineData("http://host:8765/v1")]
    [InlineData("http://host:8765?x=1")]
    [InlineData("http://host:8765#f")]
    public void RejectsUnusableEndpoints(string value)
    {
        Assert.False(BrokerEndpoint.TryParse(value, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsHttpsBecauseTheListenerIsPlaintext()
    {
        // It would parse and then never connect, which is a worse answer than
        // refusing it at the point the operator writes it.
        Assert.False(BrokerEndpoint.TryParse("https://host:8765", out _, out var error));

        Assert.Contains("plaintext", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNull()
        => Assert.False(BrokerEndpoint.TryParse(null, out _, out _));

    [Fact]
    public void ParseThrowsOnAnUnsupportedScheme()
        => Assert.Throws<FormatException>(() => BrokerEndpoint.Parse("tcp://127.0.0.1:1"));

    [Fact]
    public void RoundTripsThroughToString()
    {
        Assert.Equal("unix:///run/broker.sock", BrokerEndpoint.Parse("unix:///run/broker.sock").ToString());
        Assert.Equal("http://host:8765/", BrokerEndpoint.Parse("http://host:8765").ToString());
    }
}
