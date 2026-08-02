using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class InstallationTokenCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private static InstallationToken Token(DateTimeOffset expiresAt, string value = "t")
    {
        Assert.True(InstallationToken.TryCreate(value, expiresAt, Now, out var token, out _));
        return token;
    }

    [Fact]
    public async Task ReusesATokenThatIsStillFresh()
    {
        TestTimeProvider clock = new(Now);
        InstallationTokenCache cache = new(clock, Margin);
        var policy = TestPolicies.Policy();
        var mints = 0;

        Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            mints++;
            return Task.FromResult(Token(clock.GetUtcNow().AddHours(1)));
        }

        await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(50));
        await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);

        Assert.Equal(1, mints);
    }

    [Fact]
    public async Task MintsAgainOnceTheMarginIsReached()
    {
        TestTimeProvider clock = new(Now);
        InstallationTokenCache cache = new(clock, Margin);
        var policy = TestPolicies.Policy();
        var mints = 0;

        Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            mints++;
            return Task.FromResult(Token(clock.GetUtcNow().AddHours(1)));
        }

        await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);

        // Five minutes remain, which is not more than the margin.
        clock.Advance(TimeSpan.FromMinutes(55));
        await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);

        Assert.Equal(2, mints);
    }

    [Fact]
    public async Task KeepsGrantsForDifferentCeilingsApart()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var read = TestPolicies.Policy("owner/repo", ("contents", "read"));
        var write = TestPolicies.Policy("owner/repo", ("contents", "write"));
        var mints = 0;

        await cache.GetOrIssueAsync(read, Issue, CancellationToken.None);
        await cache.GetOrIssueAsync(write, Issue, CancellationToken.None);

        // A narrower token must never satisfy a request for a broader one.
        Assert.Equal(2, mints);
        return;

        Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            mints++;
            return Task.FromResult(Token(Now.AddHours(1)));
        }
    }

    [Fact]
    public async Task CoalescesConcurrentMissesOntoOneMint()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var policy = TestPolicies.Policy();
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var mints = 0;

        var callers = Enumerable.Range(0, 20)
            .Select(_ => cache.GetOrIssueAsync(policy, Issue, CancellationToken.None))
            .ToArray();

        release.SetResult(true);
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, mints);
        Assert.All(results, token => Assert.Same(results[0], token));
        return;

        async Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            Interlocked.Increment(ref mints);
            await release.Task;
            return Token(Now.AddHours(1));
        }
    }

    [Fact]
    public async Task AFailedMintDoesNotPoisonLaterRequests()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var policy = TestPolicies.Policy();
        var attempts = 0;

        await Assert.ThrowsAsync<TokenIssuanceException>(() => cache.GetOrIssueAsync(policy, Issue, CancellationToken.None));

        var recovered =
            await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal("t", recovered.Value);
        return;

        Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<InstallationToken>(new TokenIssuanceException(TokenIssuanceFailure.Unavailable, "first attempt"))
                : Task.FromResult(Token(Now.AddHours(1)));
        }
    }

    [Fact]
    public async Task AFailedMintFailsOnlyTheCallersWaitingOnIt()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var policy = TestPolicies.Policy();
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;

        var waiters = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrIssueAsync(policy, Issue, CancellationToken.None))
            .ToArray();

        release.SetResult(true);

        foreach (var waiter in waiters)
        {
            await Assert.ThrowsAsync<TokenIssuanceException>(() => waiter);
        }

        Assert.Equal(1, attempts);
        await cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);
        Assert.Equal(2, attempts);
        return;

        async Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt != 1)
            {
                return Token(Now.AddHours(1));
            }

            await release.Task;
            throw new TokenIssuanceException(TokenIssuanceFailure.Unavailable, "shared failure");

        }
    }

    [Fact]
    public async Task OneCallerGivingUpDoesNotCancelTheSharedMint()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var policy = TestPolicies.Policy();
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var mints = 0;

        using CancellationTokenSource abandoning = new();
        var abandoned = cache.GetOrIssueAsync(policy, Issue, abandoning.Token);
        var patient = cache.GetOrIssueAsync(policy, Issue, CancellationToken.None);

        await started.Task;
        await abandoning.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        release.SetResult(true);

        // The remaining caller still gets the token the shared mint produced.
        Assert.Equal("t", (await patient).Value);
        Assert.Equal(1, mints);
        return;

        async Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            Interlocked.Increment(ref mints);
            started.TrySetResult(true);
            await release.Task;
            return Token(Now.AddHours(1));
        }
    }

    [Fact]
    public async Task ClearDropsEveryGrant()
    {
        InstallationTokenCache cache = new(new TestTimeProvider(Now), Margin);
        var first = TestPolicies.Policy("owner/one");
        var second = TestPolicies.Policy("owner/two");
        var mints = 0;

        await cache.GetOrIssueAsync(first, Issue, CancellationToken.None);
        await cache.GetOrIssueAsync(second, Issue, CancellationToken.None);
        cache.Clear();
        await cache.GetOrIssueAsync(first, Issue, CancellationToken.None);
        await cache.GetOrIssueAsync(second, Issue, CancellationToken.None);

        Assert.Equal(4, mints);
        return;

        Task<InstallationToken> Issue(RepositoryAccessPolicy _)
        {
            mints++;
            return Task.FromResult(Token(Now.AddHours(1)));
        }
    }
}
