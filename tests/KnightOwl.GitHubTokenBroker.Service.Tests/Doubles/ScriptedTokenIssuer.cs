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

    private readonly Queue<TokenIssuanceFailure> _scripted = new();

    private int _mints;

    /// <summary>The failure to throw, or <see langword="null"/> to mint.</summary>
    /// <remarks>
    /// Read after <see cref="FailOnce"/> has run dry, so a sticky failure and a
    /// sequence can be set on one issuer.
    /// </remarks>
    public TokenIssuanceFailure? Failure { get; set; }

    /// <summary>How many mints reached this issuer.</summary>
    public int Mints => Volatile.Read(ref _mints);

    /// <summary>The token the most recent mint was given, for sequential tests.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Runs before the mint answers, so a test can make an attempt cost time.</summary>
    public Action? OnMint { get; set; }

    /// <summary>Queues one failure, to be thrown by the next mint and no later one.</summary>
    /// <param name="failure">What that mint earns.</param>
    /// <returns>This issuer, so a sequence reads as one expression.</returns>
    public ScriptedTokenIssuer FailOnce(TokenIssuanceFailure failure)
    {
        _scripted.Enqueue(failure);
        return this;
    }

    /// <inheritdoc/>
    public Task<InstallationToken> IssueAsync(
        RepositoryAccessPolicy policy,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref _mints);
        this.LastCancellationToken = cancellationToken;
        this.OnMint?.Invoke();

        if (_scripted.TryDequeue(out var scripted))
        {
            return Task.FromException<InstallationToken>(
                new TokenIssuanceException(scripted, "scripted failure")
            );
        }

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
