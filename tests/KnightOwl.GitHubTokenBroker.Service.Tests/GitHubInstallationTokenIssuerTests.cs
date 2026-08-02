using System.Net;
using System.Text.Json;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;
using Microsoft.Extensions.Logging.Abstractions;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class GitHubInstallationTokenIssuerTests : IDisposable
{
    private const long InstallationId = 789012;

    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly System.Security.Cryptography.RSA _key = TestKeys.CreateKey();

    public void Dispose()
        => _key.Dispose();

    private static string SuccessBody(
        string token = "ghs_opaque",
        string expiresAt = "2026-07-31T13:00:00Z",
        string repositoryFullName = "example-owner/example-repo",
        string repositorySelection = "selected",
        object? permissions = null
    )
        => JsonSerializer.Serialize(
            new
            {
                token,
                expires_at = expiresAt,
                permissions = permissions
                    ?? new Dictionary<string, string>
                    {
                        ["contents"] = "write",
                        ["checks"] = "read",
                    },
                repository_selection = repositorySelection,
                repositories = new[]
                {
                    new
                    {
                        id = 1L,
                        name = repositoryFullName.Split('/')[1],
                        full_name = repositoryFullName,
                    },
                },
            }
        );

    private (IInstallationTokenIssuer Issuer, StubHttpMessageHandler Handler) Build(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        StubHttpMessageHandler handler = new(respond);
        HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };

        GitHubInstallationTokenIssuer issuer = new(
            client,
            new AppJwtFactory(new StubPrivateKeySource(_key), new TestTimeProvider(Now), 123456),
            new TestTimeProvider(Now),
            InstallationId,
            NullLogger<GitHubInstallationTokenIssuer>.Instance
        );

        return (issuer, handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task SendsExactlyOneRepositoryByBareNameAndTheConfiguredPermissions()
    {
        var (issuer, handler) =
            Build((_, _) => Json(HttpStatusCode.Created, SuccessBody()));

        await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"https://api.github.com/app/installations/{InstallationId}/access_tokens",
            request.RequestUri!.AbsoluteUri
        );

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));

        // The owner is implied by the installation, so only the bare name is sent.
        var repositories = body.RootElement.GetProperty("repositories");
        Assert.Equal(1, repositories.GetArrayLength());
        Assert.Equal("example-repo", repositories[0].GetString());

        var permissions = body.RootElement.GetProperty("permissions");
        Assert.Equal(2, permissions.EnumerateObject().Count());
        Assert.Equal("write", permissions.GetProperty("contents").GetString());
        Assert.Equal("read", permissions.GetProperty("checks").GetString());
    }

    [Fact]
    public async Task NarrowsPermissionsToTheRequestedCeiling()
    {
        var (issuer, handler) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(
                    permissions: new Dictionary<string, string>
                    {
                        ["checks"] = "read",
                    }
                )
            )
        );

        await issuer.IssueAsync(
            TestPolicies.Policy("example-owner/example-repo", ("checks", "read")),
            CancellationToken.None
        );

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var permissions = body.RootElement.GetProperty("permissions");

        Assert.Single(permissions.EnumerateObject());
        Assert.Equal("read", permissions.GetProperty("checks").GetString());
    }

    [Theory]
    [InlineData("ghs_short")]
    [InlineData("v1.0123456789abcdef")]
    [InlineData("a")]
    public async Task TreatsTheTokenValueAsOpaque(string token)
    {
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, SuccessBody(token: token)));

        var issued =
            await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        Assert.Equal(token, issued.Value);
    }

    [Fact]
    public async Task AcceptsAVeryLongToken()
    {
        string token = new('x', 4096);
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, SuccessBody(token: token)));

        var issued =
            await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        Assert.Equal(token, issued.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TokenIssuanceFailure.AppUnauthorized)]
    [InlineData(HttpStatusCode.Forbidden, TokenIssuanceFailure.InstallationForbidden)]
    [InlineData(HttpStatusCode.NotFound, TokenIssuanceFailure.InstallationOrRepositoryMissing)]
    [InlineData(HttpStatusCode.UnprocessableContent, TokenIssuanceFailure.PermissionDrift)]
    [InlineData(HttpStatusCode.InternalServerError, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.OK, TokenIssuanceFailure.Unavailable)]
    public async Task ClassifiesGitHubStatuses(
        HttpStatusCode status,
        TokenIssuanceFailure expected
    )
    {
        var (issuer, _) = Build((_, _) => Json(status, "{}"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(expected, failure.Failure);
    }

    [Fact]
    public async Task ReportsATimeoutAsUnavailable()
    {
        var (issuer, _) = Build((_, _) => throw new TaskCanceledException(
                "timed out",
                new TimeoutException()
            )
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.Unavailable, failure.Failure);
    }

    [Fact]
    public async Task ReportsATransportFailureAsUnavailable()
    {
        var (issuer, _) = Build((_, _) => throw new HttpRequestException("connection refused"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.Unavailable, failure.Failure);
    }

    /// <param name="body">What GitHub answered a mint with.</param>
    /// <remarks>
    /// Malformed JSON, a missing token, a missing expiry, an expiry already
    /// past, and an empty body. Each fails the proof that the narrowing held,
    /// which is what the response has to establish before a token is returned.
    /// </remarks>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "expires_at": "2026-07-31T13:00:00Z", "repository_selection": "selected" }""")]
    [InlineData("""{ "token": "t", "repository_selection": "selected" }""")]
    [InlineData("""{ "token": "t", "expires_at": "2020-01-01T00:00:00Z", "repository_selection": "selected", "permissions": {}, "repositories": [] }""")]
    [InlineData("null")]
    public async Task RefusesAnUnusableResponse(string body)
    {
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, body));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    /// <param name="token">A token value GitHub might return.</param>
    /// <remarks>
    /// A client hands the token to line-based protocols, so a newline in it
    /// would end the value early and append fields of someone else's choosing.
    /// </remarks>
    [Theory]
    [InlineData("ghs_x\nusername=attacker")]
    [InlineData("ghs_x\r\n")]
    [InlineData("ghs_x\ty")]
    [InlineData("ghs x")]
    [InlineData(" ghs_x")]
    public async Task RefusesATokenHoldingAControlCharacterOrWhitespace(string token)
    {
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, SuccessBody(token: token)));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task KeepsTheTokenOpaque()
    {
        // No prefix, length, or alphabet is assumed, so an unfamiliar shape passes.
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, SuccessBody(token: "!@#$%^&*()_+.~")));

        var token = await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        Assert.Equal("!@#$%^&*()_+.~", token.Value);
    }

    [Fact]
    public async Task RefusesATokenScopedToAllRepositories()
    {
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, SuccessBody(repositorySelection: "all")));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task RefusesAGrantForADifferentRepository()
    {
        var (issuer, _) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(repositoryFullName: "someone-else/other")
            )
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task AcceptsARepositoryThatDiffersOnlyByCase()
    {
        var (issuer, _) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(repositoryFullName: "Example-Owner/Example-Repo")
            )
        );

        var issued =
            await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        Assert.Equal("ghs_opaque", issued.Value);
    }

    [Fact]
    public async Task RefusesAGrantCoveringMoreThanOneRepository()
    {
        var body = JsonSerializer.Serialize(
            new
            {
                token = "t",
                expires_at = "2026-07-31T13:00:00Z",
                permissions = new Dictionary<string, string>
                {
                    ["contents"] = "write",
                },
                repository_selection = "selected",
                repositories = new[]
                {
                    new
                    {
                        id = 1L,
                        name = "example-repo",
                        full_name = "example-owner/example-repo",
                    },
                    new
                    {
                        id = 2L,
                        name = "other",
                        full_name = "personal-account/other",
                    },
                },
            }
        );

        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, body));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task RefusesAGrantExceedingTheCeiling()
    {
        var (issuer, _) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(
                    permissions: new Dictionary<string, string>
                    {
                        ["contents"] = "write",
                        ["checks"] = "read",

                        // Never requested, so a token carrying it is not what was asked for.
                        ["administration"] = "write",
                    }
                )
            )
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task RefusesAGrantAtAHigherLevelThanRequested()
    {
        var (issuer, _) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(
                    permissions: new Dictionary<string, string>
                    {
                        ["checks"] = "write",
                    }
                )
            )
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy("example-owner/example-repo", ("checks", "read")),
                CancellationToken.None
            )
        );

        Assert.Equal(TokenIssuanceFailure.UntrustworthyResponse, failure.Failure);
    }

    [Fact]
    public async Task ToleratesImplicitMetadataReadInTheGrant()
    {
        var (issuer, _) = Build((_, _) => Json(
                HttpStatusCode.Created,
                SuccessBody(
                    permissions: new Dictionary<string, string>
                    {
                        ["contents"] = "write",
                        ["checks"] = "read",
                        ["metadata"] = "read",
                    }
                )
            )
        );

        var issued =
            await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        Assert.Equal("ghs_opaque", issued.Value);
    }

    [Fact]
    public async Task NoFailureMessageCarriesTheAppJwt()
    {
        var (issuer, handler) = Build((_, _) => Json(HttpStatusCode.Unauthorized, "{}"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None));

        var jwt = handler.Requests[0].Headers.Authorization!.Parameter!;
        Assert.DoesNotContain(jwt, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using CancellationTokenSource canceled = new();
        await canceled.CancelAsync();

        var (issuer, _) = Build((_, _) => throw new TaskCanceledException("canceled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => issuer.IssueAsync(TestPolicies.Policy(), canceled.Token));
    }
}
