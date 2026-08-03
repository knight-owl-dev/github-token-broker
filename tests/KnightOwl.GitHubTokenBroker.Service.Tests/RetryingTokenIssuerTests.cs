using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Resilience;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;
using Microsoft.Extensions.Logging.Abstractions;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

/// <summary>
/// Which failures earn another attempt, and what stops the attempts.
/// </summary>
/// <remarks>
/// The clock is moved by the issuer double: each attempt advances it, so a budget is
/// spent with no real delay. The wait between attempts is zero for the same reason,
/// <see cref="TestTimeProvider"/> answering only
/// <see cref="TimeProvider.GetUtcNow"/> and so never firing a timer.
/// </remarks>
public sealed class RetryingTokenIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A budget that stops bounding hangs the suite rather than failing it. These tests
    /// take under a millisecond each, so this only ever fires on that.
    /// </summary>
    private const int LoopGuardMs = 5000;

    /// <summary>Its own budget, so retuning the shipped one cannot move these counts.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    private readonly ScriptedTokenIssuer _inner = new();
    private readonly TestTimeProvider _clock = new(Now);

    /// <remarks>
    /// One attempt is all a success costs, so the wrapper adds nothing to the path
    /// every request takes.
    /// </remarks>
    [Fact]
    public async Task MintsOnceWhenTheFirstAttemptSucceeds()
    {
        var issued = await Build().IssueAsync(
            TestPolicies.Policy(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, _inner.Mints);
        Assert.Equal(ScriptedTokenIssuer.TokenValue, issued.Value);
    }

    /// <remarks>The whole of what this buys: a push that would have failed.</remarks>
    [Fact]
    public async Task RecoversAMintThatFailedForWantOfGitHub()
    {
        _inner.FailOnce(TokenIssuanceFailure.Unavailable);

        var issued = await Build().IssueAsync(
            TestPolicies.Policy(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, _inner.Mints);
        Assert.Equal(ScriptedTokenIssuer.TokenValue, issued.Value);
    }

    /// <param name="failure">A failure another attempt cannot clear.</param>
    /// <remarks>
    /// The paired half of the test above: the failure class is the only difference, so
    /// neither can pass for a decorator that retries everything or nothing.
    /// </remarks>
    [Theory]
    [InlineData(TokenIssuanceFailure.AppUnauthorized)]
    [InlineData(TokenIssuanceFailure.InstallationForbidden)]
    [InlineData(TokenIssuanceFailure.InstallationOrRepositoryMissing)]
    [InlineData(TokenIssuanceFailure.PermissionDrift)]
    [InlineData(TokenIssuanceFailure.UntrustworthyResponse)]
    [InlineData(TokenIssuanceFailure.PrivateKeyUnusable)]
    [InlineData(TokenIssuanceFailure.TimedOut)]
    [InlineData(TokenIssuanceFailure.RateLimited)]
    [InlineData(TokenIssuanceFailure.UnrecognizedStatus)]
    public async Task GivesUpOnAFailureAnotherAttemptCannotClear(TokenIssuanceFailure failure)
    {
        _inner.FailOnce(failure);

        var thrown = await Assert.ThrowsAsync<TokenIssuanceException>(() => Build().IssueAsync(
                TestPolicies.Policy(),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(1, _inner.Mints);
        Assert.Equal(failure, thrown.Failure);
    }

    /// <remarks>
    /// Bounded by elapsed time, so one attempt that eats the whole budget ends the run
    /// on its own.
    /// </remarks>
    [Fact(Timeout = LoopGuardMs)]
    public async Task StopsOnceAnAttemptHasSpentTheBudget()
    {
        _inner.Failure = TokenIssuanceFailure.Unavailable;
        _inner.OnMint = () => _clock.Advance(Budget);

        var thrown = await Assert.ThrowsAsync<TokenIssuanceException>(() => Build().IssueAsync(
                TestPolicies.Policy(),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(1, _inner.Mints);
        Assert.Equal(TokenIssuanceFailure.Unavailable, thrown.Failure);
    }

    /// <remarks>
    /// The budget is spent across attempts rather than reset by each, so a run of fast
    /// failures ends too. A third of it per attempt leaves the third with nothing.
    /// </remarks>
    [Fact(Timeout = LoopGuardMs)]
    public async Task StopsOnceAnyNumberOfAttemptsHaveSpentTheBudget()
    {
        _inner.Failure = TokenIssuanceFailure.Unavailable;
        _inner.OnMint = () => _clock.Advance(Budget / 3);

        await Assert.ThrowsAsync<TokenIssuanceException>(() => Build().IssueAsync(
                TestPolicies.Policy(),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(3, _inner.Mints);
    }

    [Fact(Timeout = LoopGuardMs)]
    public async Task StopsWhenTheBudgetCannotAffordTheWaitBeforeAnotherAttempt()
    {
        var delay = TimeSpan.FromMilliseconds(100);

        _inner.Failure = TokenIssuanceFailure.Unavailable;
        _inner.OnMint = () => _clock.Advance(Budget - (delay / 2));

        await Assert.ThrowsAsync<TokenIssuanceException>(() => Build(delay).IssueAsync(
                TestPolicies.Policy(),
                TestContext.Current.CancellationToken
            )
        );

        // Half the wait is left, so a budget that ignored it would attempt again.
        Assert.Equal(1, _inner.Mints);
    }

    /// <remarks>
    /// The class changes across the run, so the one reported can only have come from
    /// the last attempt.
    /// </remarks>
    [Fact]
    public async Task ReportsTheFailureTheLastAttemptEarned()
    {
        _inner.FailOnce(TokenIssuanceFailure.Unavailable)
            .FailOnce(TokenIssuanceFailure.AppUnauthorized);

        var thrown = await Assert.ThrowsAsync<TokenIssuanceException>(() => Build().IssueAsync(
                TestPolicies.Policy(),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(2, _inner.Mints);
        Assert.Equal(TokenIssuanceFailure.AppUnauthorized, thrown.Failure);
    }

    /// <summary>Builds the decorator.</summary>
    /// <param name="delay">The wait between attempts, none by default.</param>
    /// <returns>The issuer under test.</returns>
    private RetryingTokenIssuer Build(TimeSpan delay = default)
        => new(
            _inner,
            new RetryBudget(_clock, Budget, delay),
            NullLogger<RetryingTokenIssuer>.Instance
        );
}
