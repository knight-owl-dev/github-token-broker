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
    /// <summary>What <see cref="TestPolicies.Policy"/> mints against unless told otherwise.</summary>
    private const long InstallationId = TestPolicies.DefaultInstallation;

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

    private (IInstallationTokenIssuer Issuer, StubHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, string, HttpResponseMessage> respond
    )
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
    public async Task MintsAgainstThePolicysOwnInstallation()
    {
        const long overridden = 345678;

        var (issuer, handler) =
            Build((_, _) => Json(HttpStatusCode.Created, SuccessBody()));

        await issuer.IssueAsync(
            TestPolicies.PolicyIn(overridden),
            CancellationToken.None
        );

        Assert.Equal(
            $"https://api.github.com/app/installations/{overridden}/access_tokens",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri
        );
    }

    [Fact]
    public async Task SendsExactlyOneRepositoryByBareNameAndTheConfiguredPermissions()
    {
        var (issuer, handler) =
            Build((_, _) => Json(HttpStatusCode.Created, SuccessBody()));

        await issuer.IssueAsync(TestPolicies.Policy(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);

        // Paired with MintsAgainstThePolicysOwnInstallation, which expects a different
        // number: a route ignoring the policy would satisfy this assertion alone.
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

    /// <param name="status">The status GitHub answered with.</param>
    /// <param name="expected">The class it earns.</param>
    /// <remarks>
    /// The 5xx arm is a closed list, so every member of it is here and 507 is not. A
    /// status off the list is the broker and whatever answered it disagreeing about
    /// the API, which puts an unasked-for <see cref="HttpStatusCode.OK"/> beside
    /// <see cref="HttpStatusCode.BadRequest"/>.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TokenIssuanceFailure.AppUnauthorized)]
    [InlineData(HttpStatusCode.Forbidden, TokenIssuanceFailure.InstallationForbidden)]
    [InlineData(HttpStatusCode.NotFound, TokenIssuanceFailure.InstallationOrRepositoryMissing)]
    [InlineData(HttpStatusCode.UnprocessableContent, TokenIssuanceFailure.PermissionDrift)]
    [InlineData(HttpStatusCode.TooManyRequests, TokenIssuanceFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, TokenIssuanceFailure.Unavailable)]
    [InlineData(HttpStatusCode.OK, TokenIssuanceFailure.UnrecognizedStatus)]
    [InlineData(HttpStatusCode.BadRequest, TokenIssuanceFailure.UnrecognizedStatus)]
    [InlineData(HttpStatusCode.InsufficientStorage, TokenIssuanceFailure.UnrecognizedStatus)]
    public async Task ClassifiesGitHubStatuses(
        HttpStatusCode status,
        TokenIssuanceFailure expected
    )
    {
        var (issuer, _) = Build((_, _) => Json(status, "{}"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        Assert.Equal(expected, failure.Failure);
    }

    [Fact]
    public async Task ReportsAnUnusableKeyWithoutCallingGitHub()
    {
        StubHttpMessageHandler handler = new((_, _) => Json(HttpStatusCode.OK, SuccessBody()));
        using HttpClient client = new(handler);

        client.BaseAddress = new Uri("https://api.github.com/");

        GitHubInstallationTokenIssuer issuer = new(
            client,
            new AppJwtFactory(new UnreadablePrivateKeySource(), new TestTimeProvider(Now), 123456),
            new TestTimeProvider(Now),
            NullLogger<GitHubInstallationTokenIssuer>.Instance
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        Assert.Equal(TokenIssuanceFailure.PrivateKeyUnusable, failure.Failure);

        // The handler answers a valid token, so an empty list is the request never
        // being made rather than the response being rejected.
        Assert.Empty(handler.Requests);
    }

    /// <param name="status">A refusal that leaves what answered it in doubt.</param>
    /// <remarks>
    /// Both statuses a wrong <c>api_url</c> produces: a 404 that reads as a missing
    /// installation, and whatever a proxy in the way answers.
    /// </remarks>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task NamesTheUrlItCalledWhenWhatAnsweredIsInDoubt(HttpStatusCode status)
    {
        var (issuer, handler) = Build((_, _) => Json(status, "{}"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        // The message is checked against the URL read off the request, so it cannot pass
        // by naming one that merely looks right.
        var requested = Assert.Single(handler.Requests).RequestUri;
        Assert.NotNull(requested);

        var called = requested.AbsoluteUri;

        Assert.Equal($"https://api.github.com/app/installations/{InstallationId}/access_tokens", called);
        Assert.Contains(called, failure.Message, StringComparison.Ordinal);
    }

    /// <param name="header">The header GitHub signals the limit through.</param>
    /// <param name="value">Its value.</param>
    /// <remarks>
    /// GitHub spends 403 on a suspended installation and on a secondary rate limit
    /// alike, and only a header separates them. Both headers get a case because either
    /// alone is enough; <c>IsRateLimited</c> says why.
    /// </remarks>
    [Theory]
    [InlineData("retry-after", "60")]
    [InlineData("x-ratelimit-remaining", "0")]
    public async Task ReadsARateLimitedRefusalApartFromASuspendedInstallation(
        string header,
        string value
    )
    {
        var (issuer, _) = Build((_, _) =>
            {
                var response = Json(HttpStatusCode.Forbidden, "{}");
                response.Headers.TryAddWithoutValidation(header, value);
                return response;
            }
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        // The bare 403 in ClassifiesGitHubStatuses is the paired half: the headers are
        // the only difference, so this cannot pass for an issuer that ignores them.
        Assert.Equal(TokenIssuanceFailure.RateLimited, failure.Failure);
    }

    /// <remarks>A spent budget is not a failure another attempt recovers.</remarks>
    [Fact]
    public async Task ReportsATimeoutApartFromAnOutage()
    {
        var (issuer, _) = Build((_, _) => throw new TaskCanceledException(
                "timed out",
                new TimeoutException()
            )
        );

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        Assert.Equal(TokenIssuanceFailure.TimedOut, failure.Failure);
    }

    [Fact]
    public async Task ReportsATransportFailureAsUnavailable()
    {
        var (issuer, _) = Build((_, _) => throw new HttpRequestException("connection refused"));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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
    [InlineData(
        """{ "token": "t", "expires_at": "2020-01-01T00:00:00Z", "repository_selection": "selected", "permissions": {}, "repositories": [] }"""
    )]
    [InlineData("null")]
    public async Task RefusesAnUnusableResponse(string body)
    {
        var (issuer, _) = Build((_, _) => Json(HttpStatusCode.Created, body));

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

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

        var failure = await Assert.ThrowsAsync<TokenIssuanceException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                CancellationToken.None
            )
        );

        var jwt = handler.Requests[0].Headers.Authorization!.Parameter!;
        Assert.DoesNotContain(jwt, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PropagatesCallerCancellation()
    {
        using CancellationTokenSource canceled = new();
        await canceled.CancelAsync();

        var (issuer, _) = Build((_, _) => throw new TaskCanceledException("canceled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => issuer.IssueAsync(
                TestPolicies.Policy(),
                canceled.Token
            )
        );
    }
}
