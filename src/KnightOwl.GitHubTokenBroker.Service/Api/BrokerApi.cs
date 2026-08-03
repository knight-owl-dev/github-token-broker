using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service.Application;


namespace KnightOwl.GitHubTokenBroker.Service.Api;

/// <summary>
/// The broker's local HTTP surface.
/// </summary>
/// <remarks>
/// Client-facing bodies stay coarse while the log carries the precise reason, so a
/// caller cannot use error text to probe the allowlist. Only <c>/v1/token</c> can
/// mint.
/// </remarks>
internal static class BrokerApi
{
    /// <param name="app">The application to add the routes to.</param>
    extension(WebApplication app)
    {
        /// <summary>Maps the health, token, and check routes.</summary>
        public void MapBrokerApi()
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapGet(
                BrokerProtocol.HealthPath,
                static context =>
                {
                    var allowlist = context.RequestServices.GetRequiredService<RepositoryAllowlist>();

                    return WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        new BrokerHealthResponse
                        {
                            Status = "ok",
                            Repositories = allowlist.Count,
                        },
                        BrokerJsonContext.Default.BrokerHealthResponse
                    );
                }
            );

            app.MapPost(
                BrokerV1Routes.TokenPath,
                static async context =>
                {
                    if (await ResolveAsync(context) is not { } policy)
                    {
                        return;
                    }

                    var logger = Logger(context);
                    var tokens = context.RequestServices.GetRequiredService<TokenIssuingService>();

                    try
                    {
                        var token = await tokens.IssueAsync(policy, context.RequestAborted);

                        await WriteJsonAsync(
                            context,
                            HttpStatusCode.OK,
                            new BrokerTokenResponse
                            {
                                Token = token.Value,
                                ExpiresAt = token.ExpiresAt,
                            },
                            BrokerV1JsonContext.Default.BrokerTokenResponse
                        );
                    }
                    catch (TokenIssuanceException exception)
                    {
                        // The chain rather than the outer message: a key that went
                        // unreadable mid-flight says so only in the inner one, and
                        // the log is the whole of what the client is told to read.
                        BrokerApiLog.MintFailed(
                            logger,
                            policy.Repository.FullName,
                            exception.Failure,
                            DiagnosticReport.Describe(exception)
                        );

                        await WriteErrorAsync(
                            context,
                            StatusFor(exception.Failure),
                            "token unavailable"
                        );
                    }
                }
            );

            app.MapPost(
                BrokerV1Routes.CheckPath,
                static async context =>
                {
                    if (await ResolveAsync(context) is not { } policy)
                    {
                        return;
                    }

                    // Authorization only. Reaching here already proves the broker is
                    // reachable, its key is configured, and the repository is allowlisted.
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        new BrokerCheckResponse
                        {
                            Repository = policy.Repository.FullName,
                            Permissions = policy.Ceiling.CanonicalForm,
                        },
                        BrokerV1JsonContext.Default.BrokerCheckResponse
                    );
                }
            );
        }
    }

    /// <summary>
    /// Validates content type and body, then resolves the request against the
    /// allowlist. Writes the failure response itself and returns
    /// <see langword="null"/> when the request cannot proceed.
    /// </summary>
    /// <param name="context">The request being served.</param>
    /// <returns>The governing policy, or <see langword="null"/> once refused.</returns>
    private static async Task<RepositoryAccessPolicy?> ResolveAsync(HttpContext context)
    {
        var logger = Logger(context);

        if (!context.Request.HasJsonContentType())
        {
            await WriteErrorAsync(
                context,
                HttpStatusCode.UnsupportedMediaType,
                "expected application/json"
            );

            return null;
        }

        BrokerRepositoryRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync(
                BrokerV1JsonContext.Default.BrokerRepositoryRequest,
                context.RequestAborted
            );
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "malformed request");
            return null;
        }
        catch (BadHttpRequestException)
        {
            // Raised when the body exceeds the configured maximum.
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "malformed request");
            return null;
        }
        catch (InvalidOperationException)
        {
            // A charset the reader will not take. Refused here so every rejection
            // is still one status and one message rather than an unhandled 500.
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "malformed request");
            return null;
        }

        var validator = context.RequestServices.GetRequiredService<RepositoryRequestValidator>();

        if (validator.TryResolve(request?.Host, request?.Repository, out var policy, out var error))
        {
            return policy;
        }

        BrokerApiLog.RequestRefused(logger, error);

        // One status and one message for every refusal, so a caller learns
        // nothing about which repositories exist in the allowlist.
        await WriteErrorAsync(context, HttpStatusCode.Forbidden, "not authorized");
        return null;
    }

    /// <summary>Maps a classified mint failure onto the status a client sees.</summary>
    /// <param name="failure">Why the mint failed.</param>
    /// <returns>The status to send.</returns>
    /// <remarks>
    /// <see cref="HttpStatusCode.Conflict"/> covers the failures that persist until
    /// an operator acts, so a client can tell them from an outage it should retry.
    /// A rejected App credential is one of them: only clock skew clears without anyone
    /// acting, so retrying the rest never succeeds.
    /// </remarks>
    private static HttpStatusCode StatusFor(TokenIssuanceFailure failure)
        => failure switch
        {
            TokenIssuanceFailure.AppUnauthorized => HttpStatusCode.Conflict,
            TokenIssuanceFailure.InstallationForbidden => HttpStatusCode.Conflict,
            TokenIssuanceFailure.InstallationOrRepositoryMissing => HttpStatusCode.Conflict,
            TokenIssuanceFailure.PermissionDrift => HttpStatusCode.InternalServerError,
            TokenIssuanceFailure.UntrustworthyResponse => HttpStatusCode.InternalServerError,
            TokenIssuanceFailure.PrivateKeyUnusable => HttpStatusCode.InternalServerError,
            TokenIssuanceFailure.Unavailable => HttpStatusCode.ServiceUnavailable,
        };

    private static ILogger Logger(HttpContext context)
        => context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(BrokerApi));

    private static Task WriteErrorAsync(HttpContext context, HttpStatusCode status, string error)
        => WriteJsonAsync(
            context,
            status,
            new BrokerErrorResponse
            {
                Error = error,
            },
            BrokerJsonContext.Default.BrokerErrorResponse
        );

    /// <summary>
    /// Writes a body using its generated contract. The type information is passed
    /// explicitly because ahead-of-time publishing has no reflective fallback.
    /// </summary>
    /// <typeparam name="TBody">The body type.</typeparam>
    /// <param name="context">The request being served.</param>
    /// <param name="status">The status to send.</param>
    /// <param name="body">The body to serialize.</param>
    /// <param name="typeInfo">Generated contract for <typeparamref name="TBody"/>.</param>
    private static async Task WriteJsonAsync<TBody>(
        HttpContext context,
        HttpStatusCode status,
        TBody body,
        JsonTypeInfo<TBody> typeInfo
    )
    {
        context.Response.StatusCode = (int) status;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, typeInfo),
            context.RequestAborted
        );
    }
}
