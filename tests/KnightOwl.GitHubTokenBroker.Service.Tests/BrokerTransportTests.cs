using System.Net;
using System.Runtime.Versioning;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;
using KnightOwl.GitHubTokenBroker.Service.Api;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

/// <summary>
/// Drives the real client against the real server over the socket, so the
/// transport is proven rather than stubbed.
/// </summary>
public sealed class BrokerTransportTests : IAsyncLifetime
{
    private const string Token = ScriptedTokenIssuer.TokenValue;
    private const UnixFileMode SocketMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly RepositoryName Allowlisted =
        RepositoryName.Parse("example-owner/example-repo");

    private readonly string _root = Directory.CreateTempSubdirectory("hga-tx").FullName;

    // Under /tmp (rather than _root) with a truncated GUID: see ListenValidator for the limit.
    private readonly string _socketPath = Path.Combine("/tmp", $"hga-{Guid.NewGuid():N}"[..20] + ".sock");
    private readonly ScriptedTokenIssuer _issuer = new();

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(TestPolicies.Allowlist());
        builder.Services.AddSingleton(GitHubHost.GitHubCom);
        builder.Services.AddSingleton(services => new RepositoryRequestValidator(
                services.GetRequiredService<GitHubHost>(),
                services.GetRequiredService<RepositoryAllowlist>()
            )
        );

        builder.Services.AddSingleton(services => new InstallationTokenCache(
                services.GetRequiredService<TimeProvider>(),
                TimeSpan.FromMinutes(5)
            )
        );

        builder.Services.AddSingleton<IInstallationTokenIssuer>(_issuer);
        builder.Services.AddSingleton<TokenIssuingService>();

        // The composition root's own listener setup, so the body limit and the socket
        // are the ones the broker runs with.
        builder.WebHost.UseBrokerListeners(new ListenOptions(new UnixSocketOptions(_socketPath, SocketMode)));

        _app = builder.Build();
        _app.MapBrokerApi();
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        File.Delete(_socketPath);
        Directory.Delete(_root, recursive: true);
    }

    private BrokerEndpoint UnixSocketEndpoint()
        => BrokerEndpoint.Parse($"unix://{_socketPath}");

    private HttpBrokerClient UnixClient()
        => HttpBrokerClient.Create(UnixSocketEndpoint());

    [Fact]
    public async Task ServesATokenOverAUnixSocket()
    {
        using var client = UnixClient();

        var response = await client.RequestTokenAsync(Allowlisted, CancellationToken.None);

        Assert.Equal(Token, response.Token);
    }

    [Fact]
    public async Task ServesACheckWithoutACredential()
    {
        // Access to the socket is a filesystem question, so no shared secret is imposed.
        using var client = UnixClient();

        var response = await client.CheckAsync(Allowlisted, CancellationToken.None);

        Assert.Equal("example-owner/example-repo", response.Repository);
    }

    [Fact]
    public async Task RefusesAnUnlistedRepository()
    {
        var unlisted = RepositoryName.Parse("other/upstream");
        using var client = UnixClient();

        var failure = await Assert.ThrowsAsync<BrokerClientException>(()
            => client.RequestTokenAsync(unlisted, CancellationToken.None)
        );

        Assert.Equal(BrokerClientFailure.Refused, failure.Failure);
    }

    /// <remarks>
    /// /health answers with a status and a repository count, so a token could not
    /// appear in its body under any implementation. The issuer's counter is where the
    /// absence is observable, and the paired token request shows it moves at all.
    /// </remarks>
    [Fact]
    public async Task HealthNeverMints()
    {
        using var raw = BrokerHttpClientFactory.Create(UnixSocketEndpoint());

        using var response = await raw.GetAsync(
            new Uri(BrokerProtocol.HealthPath, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.Equal(0, _issuer.Mints);

        using var client = UnixClient();
        await client.RequestTokenAsync(Allowlisted, TestContext.Current.CancellationToken);

        Assert.Equal(1, _issuer.Mints);
    }

    /// <param name="failure">How the mint failed.</param>
    /// <param name="expected">The status a client is answered with.</param>
    /// <remarks>
    /// The middle of the failure chain, which the issuer's own tests and the client's
    /// each stop short of: a classified mint failure becomes the status a client reads.
    /// Every class is listed, so moving one between buckets fails here.
    /// </remarks>
    [Theory]
    [InlineData(TokenIssuanceFailure.AppUnauthorized, HttpStatusCode.Conflict)]
    [InlineData(TokenIssuanceFailure.InstallationForbidden, HttpStatusCode.Conflict)]
    [InlineData(TokenIssuanceFailure.InstallationOrRepositoryMissing, HttpStatusCode.Conflict)]
    [InlineData(TokenIssuanceFailure.PermissionDrift, HttpStatusCode.InternalServerError)]
    [InlineData(TokenIssuanceFailure.UntrustworthyResponse, HttpStatusCode.InternalServerError)]
    [InlineData(TokenIssuanceFailure.PrivateKeyUnusable, HttpStatusCode.InternalServerError)]
    [InlineData(TokenIssuanceFailure.UnrecognizedStatus, HttpStatusCode.InternalServerError)]
    [InlineData(TokenIssuanceFailure.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(TokenIssuanceFailure.TimedOut, HttpStatusCode.ServiceUnavailable)]
    [InlineData(TokenIssuanceFailure.RateLimited, HttpStatusCode.ServiceUnavailable)]
    public async Task ClassifiesMintFailures(TokenIssuanceFailure failure, HttpStatusCode expected)
    {
        _issuer.Failure = failure;
        using var raw = BrokerHttpClientFactory.Create(UnixSocketEndpoint());

        using var response = await raw.PostAsync(
            new Uri(BrokerV1Routes.TokenPath, UriKind.Relative),
            new StringContent(
                $"{{\"host\":\"github.com\",\"repository\":\"{Allowlisted.FullName}\"}}",
                System.Text.Encoding.UTF8,
                "application/json"
            ),
            TestContext.Current.CancellationToken
        );

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);

        // The status is the whole of what a client is told; the reason is the log's.
        Assert.Equal("{\"error\":\"token unavailable\"}", body);
    }

    [Fact]
    public async Task RefusesABodyThatIsNotJson()
    {
        using var raw = BrokerHttpClientFactory.Create(UnixSocketEndpoint());

        using var response = await raw.PostAsync(
            new Uri(BrokerV1Routes.TokenPath, UriKind.Relative),
            new StringContent("host=github.com", System.Text.Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task RefusesAnOversizedBody()
    {
        using var raw = BrokerHttpClientFactory.Create(UnixSocketEndpoint());

        var oversized = "{\"host\":\"github.com\",\"repository\":\""
            + new string('a', BrokerProtocol.MaxRequestBytes * 2)
            + "\"}";

        using var response = await raw.PostAsync(
            new Uri(BrokerV1Routes.TokenPath, UriKind.Relative),
            new StringContent(oversized, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"unexpected status {response.StatusCode}"
        );
    }

    [Fact]
    public async Task RefusesAGetOnTheTokenRoute()
    {
        using var raw = BrokerHttpClientFactory.Create(UnixSocketEndpoint());

        using var response = await raw.GetAsync(
            new Uri(BrokerV1Routes.TokenPath, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // The broker publishes for osx and linux only; a Unix mode has no Windows
    // meaning, and the attribute says so where a runtime guard would leave the
    // assertions silently skipped.
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void NarrowsTheSocketItBound()
    {
        const UnixFileMode group = SocketMode | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

        var applied = BrokerListeners.NarrowUnixSocket(new UnixSocketOptions(_socketPath, group));

        Assert.Equal(group, File.GetUnixFileMode(_socketPath));
        Assert.Equal("0660", applied);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void RefusesASocketItCannotNarrow()
    {
        // The bound socket above proves the same call succeeds, so this is the
        // mode failing rather than the method never working.
        var absent = Path.Combine(_root, "absent.sock");

        var failure = Assert.Throws<UnixSocketModeException>(()
            => BrokerListeners.NarrowUnixSocket(new UnixSocketOptions(absent, SocketMode))
        );

        Assert.Contains(absent, failure.Message, StringComparison.Ordinal);
    }
}
