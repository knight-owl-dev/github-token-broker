using System.Net;
using System.Text;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

public sealed class HttpBrokerClientTests
{
    private static readonly RepositoryName Repository =
        RepositoryName.Parse("example-owner/example-repo");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            this.LastRequest = request;
            this.LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return respond(request);
        }
    }

    private static readonly BrokerEndpoint Endpoint =
        BrokerEndpoint.Parse("unix:///run/github-token-broker/broker.sock");

    private static HttpBrokerClient Client(StubHandler handler)
        => new(
            new HttpClient(handler)
            {
                BaseAddress = Endpoint.BaseUri,
            },
            Endpoint
        );

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task SendsTheHostAndRepositoryToTheTokenRoute()
    {
        StubHandler handler = new(_ => Json(
                HttpStatusCode.OK,
                """{ "token": "ghs_x", "expires_at": "2099-01-01T00:00:00Z" }"""
            )
        );

        using var client = Client(handler);

        var response =
            await client.RequestTokenAsync(Repository, CancellationToken.None);

        Assert.Equal("ghs_x", response.Token);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith(
            BrokerV1Routes.TokenPath,
            handler.LastRequest.RequestUri!.AbsolutePath,
            StringComparison.Ordinal
        );

        Assert.Contains("\"host\":\"github.com\"", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"repository\":\"example-owner/example-repo\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesTheCheckRouteWithoutRequestingAToken()
    {
        StubHandler handler = new(_ => Json(
                HttpStatusCode.OK,
                """{ "repository": "example-owner/example-repo", "permissions": "contents:write" }"""
            )
        );

        using var client = Client(handler);

        var response = await client.CheckAsync(Repository, CancellationToken.None);

        Assert.Equal("contents:write", response.Permissions);
        Assert.EndsWith(
            BrokerV1Routes.CheckPath,
            handler.LastRequest!.RequestUri!.AbsolutePath,
            StringComparison.Ordinal
        );
    }

    /// <param name="status">The status the broker answered with.</param>
    /// <param name="expected">How the client classifies it.</param>
    /// <remarks>
    /// Conflict is the broker and GitHub disagreeing about what may be minted, which
    /// retrying cannot help. ServiceUnavailable is the one an operator has nothing to
    /// do about. Both are classified apart from the statuses that say only that the
    /// broker's log holds the reason.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, BrokerClientFailure.Refused)]
    [InlineData(HttpStatusCode.Conflict, BrokerClientFailure.Misconfigured)]
    [InlineData(HttpStatusCode.ServiceUnavailable, BrokerClientFailure.Retryable)]
    [InlineData(HttpStatusCode.Unauthorized, BrokerClientFailure.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, BrokerClientFailure.Failed)]
    [InlineData(HttpStatusCode.NotFound, BrokerClientFailure.Failed)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, BrokerClientFailure.Failed)]
    public async Task ClassifiesBrokerStatuses(HttpStatusCode status, BrokerClientFailure expected)
    {
        StubHandler handler = new(_ => Json(status, """{ "error": "no" }"""));
        using var client = Client(handler);

        var failure = await Assert.ThrowsAsync<BrokerClientException>(()
            => client.RequestTokenAsync(Repository, CancellationToken.None)
        );

        Assert.Equal(expected, failure.Failure);
    }

    [Fact]
    public async Task ReportsAnUnreachableBrokerAsUnavailable()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("no such socket"));
        using var client = Client(handler);

        var failure = await Assert.ThrowsAsync<BrokerClientException>(()
            => client.RequestTokenAsync(Repository, CancellationToken.None)
        );

        Assert.Equal(BrokerClientFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task ReportsATimeoutAsUnavailable()
    {
        StubHandler handler = new(_ => throw new TaskCanceledException("timed out", new TimeoutException()));
        using var client = Client(handler);

        var failure = await Assert.ThrowsAsync<BrokerClientException>(()
            => client.RequestTokenAsync(Repository, CancellationToken.None)
        );

        Assert.Equal(BrokerClientFailure.Unavailable, failure.Failure);
    }

    /// <remarks>
    /// <see cref="Endpoint"/> is a socket, so its request authority is a
    /// placeholder; the absence of that placeholder is the assertion.
    /// </remarks>
    [Fact]
    public async Task NamesTheEndpointOnBothUnavailablePaths()
    {
        StubHandler unreachable = new(_ => throw new HttpRequestException("no route to host"));
        using var toNothing = Client(unreachable);

        var reach = await Assert.ThrowsAsync<BrokerClientException>(()
            => toNothing.RequestTokenAsync(Repository, CancellationToken.None)
        );

        StubHandler slow = new(_ => throw new TaskCanceledException("timed out", new TimeoutException()));
        using var toSilence = Client(slow);

        var timeout = await Assert.ThrowsAsync<BrokerClientException>(()
            => toSilence.RequestTokenAsync(Repository, CancellationToken.None)
        );

        foreach (var message in new[] { reach.Message, timeout.Message })
        {
            Assert.Contains(Endpoint.UnixSocketPath, message, StringComparison.Ordinal);
            Assert.DoesNotContain("localhost", message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public async Task ReportsAnUnusableBodyAsFailed(string body)
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, body));
        using var client = Client(handler);

        var failure = await Assert.ThrowsAsync<BrokerClientException>(()
            => client.RequestTokenAsync(Repository, CancellationToken.None)
        );

        Assert.Equal(BrokerClientFailure.Failed, failure.Failure);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using CancellationTokenSource canceled = new();
        await canceled.CancelAsync();

        StubHandler handler = new(_ => throw new TaskCanceledException("canceled"));
        using var client = Client(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(()
            => client.RequestTokenAsync(Repository, canceled.Token)
        );
    }
}
