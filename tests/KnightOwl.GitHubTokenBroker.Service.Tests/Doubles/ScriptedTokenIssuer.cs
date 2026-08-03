using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// Stands in for GitHub, answering with one fixed token or with a classified
/// failure the test chooses.
/// </summary>
/// <remarks>
/// Counts what reached it, which is the only way a caller can tell a cached grant from
/// a fresh mint.
/// </remarks>
internal sealed class ScriptedTokenIssuer : IInstallationTokenIssuer
{
    /// <summary>The token every successful mint answers with.</summary>
    public const string TokenValue = "ghs_opaqueTokenValue";

    private int _mints;

    /// <summary>The failure to throw, or <see langword="null"/> to mint.</summary>
    public TokenIssuanceFailure? Failure { get; set; }

    /// <summary>How many mints reached this issuer.</summary>
    public int Mints => Volatile.Read(ref _mints);

    /// <summary>The token the most recent mint was given, for sequential tests.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <inheritdoc/>
    public Task<InstallationToken> IssueAsync(
        RepositoryAccessPolicy policy,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _mints);
        this.LastCancellationToken = cancellationToken;

        if (this.Failure is { } failure)
        {
            return Task.FromException<InstallationToken>(
                new TokenIssuanceException(failure, "scripted failure")
            );
        }

        var now = DateTimeOffset.UtcNow;

        return !InstallationToken.TryCreate(TokenValue, now.AddHours(1), now, out var token, out var error)
            ? throw new InvalidOperationException(error)
            : Task.FromResult(token);
    }
}
