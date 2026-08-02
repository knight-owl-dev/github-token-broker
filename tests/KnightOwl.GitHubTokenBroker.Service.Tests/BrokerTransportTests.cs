using System.Net;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;
using KnightOwl.GitHubTokenBroker.Service.Api;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

/// <summary>
/// Drives the real client against the real server over both transports, so the
/// endpoint form is proven not to change any behavior above it.
/// </summary>
public sealed class BrokerTransportTests : IAsyncLifetime
{
    private const string Credential = "0123456789abcdef0123456789abcdef";
    private const string Token = "ghs_opaqueTokenValue";

    private static readonly RepositoryName Allowlisted =
        RepositoryName.Parse("example-owner/example-repo");

    private readonly string _root = Directory.CreateTempSubdirectory("hga-tx").FullName;
    private readonly string _credentialPath;
    private readonly string _socketPath;

    private WebApplication? _app;
    private int _tcpPort;

    public BrokerTransportTests()
    {
        _credentialPath = Path.Combine(_root, "credential");
        File.WriteAllText(_credentialPath, Credential);

        // Short by necessity: a socket path is limited to about 104 bytes.
        _socketPath = Path.Combine("/tmp", $"hga-{Guid.NewGuid():N}"[..20] + ".sock");
    }

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

        builder.Services.AddSingleton<IInstallationTokenIssuer, FixedTokenIssuer>();
        builder.Services.AddSingleton<TokenIssuingService>();
        builder.Services.AddSingleton(ClientCredential.Load(_credentialPath));

        UnixSocketPreparation.Prepare(_socketPath);
        builder.WebHost.ConfigureKestrel(options =>
            {
                // Mirrors the composition root: the body limit is a server setting, so an
                // endpoint-only test host would not enforce it.
                options.Limits.MaxRequestBodySize = BrokerProtocol.MaxRequestBytes;

                options.ListenUnixSocket(_socketPath);
                options.Listen(
                    IPAddress.Loopback,
                    0,
                    listen => listen.Use(next => async connection =>
                        {
                            connection.Features.Set(new TcpTransportMarker());
                            await next(connection);
                        }
                    )
                );
            }
        );

        _app = builder.Build();
        _app.UseClientCredentialGate();
        _app.MapBrokerApi();
        await _app.StartAsync();

        _tcpPort = new Uri(_app.Urls.Single(url => url.StartsWith("http://127.0.0.1", StringComparison.Ordinal)))
            .Port;
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

    private HttpBrokerClient UnixClient()
        => new(
            BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"unix://{_socketPath}")),
            clientCredential: null
        );

    private HttpBrokerClient TcpClient(string? credential)
        => new(
            BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"http://127.0.0.1:{_tcpPort}")),
            credential
        );

    [Fact]
    public async Task ServesATokenOverAUnixSocket()
    {
        using var client = UnixClient();

        var response =
            await client.RequestTokenAsync(Allowlisted, CancellationToken.None);

        Assert.Equal(Token, response.Token);
    }

    [Fact]
    public async Task ServesACheckOverAUnixSocketWithoutACredential()
    {
        // Access to the socket is a filesystem question, so no shared secret is imposed.
        using var client = UnixClient();

        var response = await client.CheckAsync(Allowlisted, CancellationToken.None);

        Assert.Equal("example-owner/example-repo", response.Repository);
    }

    [Fact]
    public async Task ServesATokenOverTcpWithTheCredential()
    {
        using var client = TcpClient(Credential);

        var response = await client.RequestTokenAsync(Allowlisted, CancellationToken.None);

        Assert.Equal(Token, response.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-credential-of-the-right-length")]
    public async Task RefusesTcpWithoutTheRightCredential(string? credential)
    {
        using var client = TcpClient(credential);

        var failure = await Assert.ThrowsAsync<BrokerClientException>(() => client.RequestTokenAsync(Allowlisted, CancellationToken.None));

        Assert.Equal(BrokerClientFailure.Unauthenticated, failure.Failure);
    }

    [Fact]
    public async Task RefusesAnUnlistedRepositoryOnBothTransports()
    {
        var unlisted = RepositoryName.Parse("other/upstream");

        using var overSocket = UnixClient();
        var socketFailure = await Assert.ThrowsAsync<BrokerClientException>(() => overSocket.RequestTokenAsync(unlisted, CancellationToken.None));
        Assert.Equal(BrokerClientFailure.Refused, socketFailure.Failure);

        using var overTcp = TcpClient(Credential);
        var tcpFailure = await Assert.ThrowsAsync<BrokerClientException>(() => overTcp.RequestTokenAsync(unlisted, CancellationToken.None));
        Assert.Equal(BrokerClientFailure.Refused, tcpFailure.Failure);
    }

    [Fact]
    public async Task HealthNeverMints()
    {
        using var raw = BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"unix://{_socketPath}"));

        using var response = await raw.GetAsync(
            new Uri(BrokerProtocol.HealthPath, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesABodyThatIsNotJson()
    {
        using var raw = BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"unix://{_socketPath}"));

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
        using var raw = BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"unix://{_socketPath}"));

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
        using var raw = BrokerHttpClientFactory.Create(BrokerEndpoint.Parse($"unix://{_socketPath}"));

        using var response = await raw.GetAsync(
            new Uri(BrokerV1Routes.TokenPath, UriKind.Relative),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>Stands in for GitHub, returning one fixed token.</summary>
    private sealed class FixedTokenIssuer : IInstallationTokenIssuer
    {
        public Task<InstallationToken> IssueAsync(
            RepositoryAccessPolicy policy,
            CancellationToken cancellationToken
        )
        {
            Assert.True(
                InstallationToken.TryCreate(
                    Token,
                    DateTimeOffset.UtcNow.AddHours(1),
                    DateTimeOffset.UtcNow,
                    out var token,
                    out _
                )
            );

            return Task.FromResult(token);
        }
    }
}
