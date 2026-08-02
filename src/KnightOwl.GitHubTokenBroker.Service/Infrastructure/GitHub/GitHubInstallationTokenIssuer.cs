using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Permissions;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;

/// <summary>
/// Mints installation tokens through GitHub's REST API.
/// </summary>
/// <remarks>
/// This is the only component that reaches GitHub. It sends exactly one repository
/// and exactly the configured permission map, then refuses to return a token whose
/// response does not prove that narrowing held.
/// </remarks>
public sealed class GitHubInstallationTokenIssuer : IInstallationTokenIssuer
{
    /// <summary>Pinned REST API version, so a future default cannot change parsing.</summary>
    private const string ApiVersion = "2022-11-28";

    private const string RepositorySelectionSelected = "selected";

    private readonly HttpClient _httpClient;
    private readonly IAppJwtFactory _appJwtFactory;
    private readonly TimeProvider _timeProvider;
    private readonly long _installationId;
    private readonly ILogger<GitHubInstallationTokenIssuer> _logger;

    /// <summary>Creates the issuer.</summary>
    /// <param name="httpClient">Client whose base address and timeout are already configured.</param>
    /// <param name="appJwtFactory">Supplies the App JWT for each attempt.</param>
    /// <param name="timeProvider">Clock used to judge the returned expiry.</param>
    /// <param name="installationId">The installation to mint against.</param>
    /// <param name="logger">Receives token-free diagnostics.</param>
    public GitHubInstallationTokenIssuer(
        HttpClient httpClient,
        IAppJwtFactory appJwtFactory,
        TimeProvider timeProvider,
        long installationId,
        ILogger<GitHubInstallationTokenIssuer> logger
    )
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(appJwtFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);

        _httpClient = httpClient;
        _appJwtFactory = appJwtFactory;
        _timeProvider = timeProvider;
        _installationId = installationId;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InstallationToken> IssueAsync(
        RepositoryAccessPolicy policy,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(policy);

        AppJwt jwt;
        try
        {
            jwt = _appJwtFactory.Create();
        }
        catch (ConfigurationException exception)
        {
            // Classified rather than left to escape, which would answer a bare 500
            // with nothing in the log naming the key.
            throw new TokenIssuanceException(
                TokenIssuanceFailure.PrivateKeyUnusable,
                "the private key could not be loaded for this mint",
                exception
            );
        }

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            string.Create(
                CultureInfo.InvariantCulture,
                $"app/installations/{_installationId}/access_tokens"
            )
        );

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        // Serialized up front so the request carries a Content-Length. A lazily
        // serialized body would be sent chunked, which is needless for a payload this
        // small and less predictable through an intermediary.
        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new InstallationTokenRequest
                {
                    // Repository names exclude the owner: the installation implies it.
                    Repositories = [policy.Repository.Name],
                    Permissions = policy.Ceiling.ToWireMap(),
                },
                GitHubJsonContext.Default.InstallationTokenRequest
            ),
            Encoding.UTF8,
            "application/json"
        );

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.Unavailable,
                "the GitHub token request timed out",
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.Unavailable,
                "the GitHub token request could not be completed",
                exception
            );
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw Classify(response.StatusCode, jwt.KeyId);
            }

            InstallationTokenResponse? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync(
                        GitHubJsonContext.Default.InstallationTokenResponse,
                        cancellationToken
                    );
            }
            catch (JsonException exception)
            {
                throw new TokenIssuanceException(
                    TokenIssuanceFailure.UntrustworthyResponse,
                    "the GitHub token response was not valid JSON",
                    exception
                );
            }

            if (body is null)
            {
                throw new TokenIssuanceException(
                    TokenIssuanceFailure.UntrustworthyResponse,
                    "the GitHub token response was empty"
                );
            }

            return Validate(body, policy, jwt.KeyId);
        }
    }

    /// <summary>
    /// Maps a GitHub status onto a classified failure. The distinctions matter:
    /// they decide whether the broker discards what it holds and what an operator
    /// is told to check.
    /// </summary>
    /// <param name="status">The status GitHub returned.</param>
    /// <param name="keyId">Identifier of the key that signed the attempt.</param>
    /// <returns>The classified failure, ready to throw.</returns>
    private TokenIssuanceException Classify(HttpStatusCode status, string keyId)
        => status switch
        {
            HttpStatusCode.Unauthorized => new TokenIssuanceException(
                TokenIssuanceFailure.AppUnauthorized,
                $"GitHub rejected the App JWT signed by key {keyId}; check the private key and host clock"
            ),
            HttpStatusCode.Forbidden => new TokenIssuanceException(
                TokenIssuanceFailure.InstallationForbidden,
                $"GitHub refused installation {_installationId}; it may be suspended"
            ),
            HttpStatusCode.NotFound => new TokenIssuanceException(
                TokenIssuanceFailure.InstallationOrRepositoryMissing,
                $"GitHub does not recognize installation {_installationId} or the requested repository"
            ),
            HttpStatusCode.UnprocessableContent => new TokenIssuanceException(
                TokenIssuanceFailure.PermissionDrift,
                "GitHub refused the requested permissions; broker configuration asks for more than the App registration grants"
            ),
            _ => new TokenIssuanceException(
                TokenIssuanceFailure.Unavailable,
                $"GitHub returned an unexpected status {(int) status}"
            ),
        };

    /// <summary>
    /// Confirms the grant is what was asked for before any caller sees the token.
    /// A response that cannot be proven narrow is treated as a failure.
    /// </summary>
    /// <param name="body">The response GitHub returned.</param>
    /// <param name="policy">The repository and ceiling that were asked for.</param>
    /// <param name="keyId">Identifier of the key that signed the attempt.</param>
    /// <returns>The token, once the grant is proven narrow.</returns>
    /// <exception cref="TokenIssuanceException">The grant could not be proven.</exception>
    private InstallationToken Validate(
        InstallationTokenResponse body,
        RepositoryAccessPolicy policy,
        string keyId
    )
    {
        if (!InstallationToken.TryCreate(
                body.Token,
                body.ExpiresAt,
                _timeProvider.GetUtcNow(),
                out var token,
                out var tokenError
            ))
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                $"the GitHub token response was unusable: {tokenError}"
            );
        }

        if (!string.Equals(
                body.RepositorySelection,
                RepositorySelectionSelected,
                StringComparison.Ordinal
            ))
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                $"GitHub reported repository_selection \"{body.RepositorySelection}\" rather than \"{RepositorySelectionSelected}\""
            );
        }

        var repositories = body.Repositories ?? [];
        if (repositories.Count != 1)
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                $"GitHub granted {repositories.Count} repositories for a single-repository request"
            );
        }

        if (!string.Equals(
                repositories[0].FullName,
                policy.Repository.FullName,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                $"GitHub granted a different repository than {policy.Repository.FullName}"
            );
        }

        ValidatePermissions(body.Permissions, policy);

        TokenIssuerLog.Minted(
            _logger,
            policy.Repository.FullName,
            policy.Ceiling.CanonicalForm,
            keyId,
            token.ExpiresAt
        );

        return token;
    }

    private static void ValidatePermissions(
        Dictionary<string, string>? granted,
        RepositoryAccessPolicy policy
    )
    {
        if (granted is null)
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                "GitHub did not report the granted permissions"
            );
        }

        Dictionary<string, PermissionLevel> parsed = new(StringComparer.Ordinal);
        foreach ((var name, var level) in granted)
        {
            if (!PermissionVocabulary.TryParseLevel(level, out var parsedLevel))
            {
                throw new TokenIssuanceException(
                    TokenIssuanceFailure.UntrustworthyResponse,
                    $"GitHub granted permission \"{name}\" at unrecognized level \"{level}\""
                );
            }

            parsed[name] = parsedLevel.Value;
        }

        if (!policy.Ceiling.Covers(parsed, out var excess))
        {
            throw new TokenIssuanceException(
                TokenIssuanceFailure.UntrustworthyResponse,
                $"the granted token exceeds its configured ceiling: {excess}"
            );
        }
    }
}
