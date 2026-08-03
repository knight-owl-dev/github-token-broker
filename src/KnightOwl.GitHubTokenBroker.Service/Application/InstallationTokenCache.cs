using System.Collections.Concurrent;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;


namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// In-memory grant cache. Entries are keyed by
/// <see cref="RepositoryAccessPolicy.GrantKey"/>, so everything that key binds
/// must match before a token is reused.
/// </summary>
/// <remarks>
/// There is no refresh loop: a token is minted on the first request that finds
/// none fresh enough. Concurrent misses for one key are coalesced onto a single
/// mint, and no lock is held while that mint is in flight. A failed mint clears
/// its own entry, so it fails only the callers already waiting on it and never
/// poisons a later request.
/// </remarks>
public sealed class InstallationTokenCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshMargin;

    /// <summary>Creates an empty cache.</summary>
    /// <param name="timeProvider">Clock used to judge freshness.</param>
    /// <param name="refreshMargin">How much life a token must have left to be reused.</param>
    public InstallationTokenCache(TimeProvider timeProvider, TimeSpan refreshMargin)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _refreshMargin = refreshMargin;
    }

    /// <summary>
    /// Returns a sufficiently fresh cached token, or mints one through
    /// <paramref name="issue"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="cancellationToken"/> abandons this caller's wait; it does
    /// not cancel a mint that other callers are also waiting on. The mint itself
    /// is bounded by the issuer's own timeout.
    /// </remarks>
    /// <param name="policy">The authorized policy to serve or mint for.</param>
    /// <param name="issue">Mints a token when no fresh one is cached.</param>
    /// <param name="cancellationToken">Abandons this caller's wait.</param>
    /// <returns>A token with more than the refresh margin remaining.</returns>
    public async Task<InstallationToken> GetOrIssueAsync(
        RepositoryAccessPolicy policy,
        Func<RepositoryAccessPolicy, Task<InstallationToken>> issue,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(issue);

        var entry = _entries.GetOrAdd(policy.GrantKey, static _ => new Entry());
        Task<InstallationToken> mint;

        lock (entry.Gate)
        {
            if (entry.Token is { } cached && cached.IsFreshAt(_timeProvider.GetUtcNow(), _refreshMargin))
            {
                return cached;
            }

            entry.InFlight ??= IssueAndStoreAsync(entry, policy, issue);
            mint = entry.InFlight;
        }

        return await mint.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Drops every stored grant. Used when configuration is reloaded or the
    /// private key rotates, since both change what a token would mean.
    /// </summary>
    /// <remarks>
    /// A mint already in flight finishes and stores its result. It only reaches
    /// that point if GitHub accepted the JWT, and the token it returns lives its
    /// full hour whatever becomes of the key.
    /// </remarks>
    public void Clear()
    {
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
                entry.Token = null;
            }
        }
    }

    private static async Task<InstallationToken> IssueAndStoreAsync(
        Entry entry,
        RepositoryAccessPolicy policy,
        Func<RepositoryAccessPolicy, Task<InstallationToken>> issue
    )
    {
        // Yield before touching the entry so this method returns to the caller
        // while it still holds the lock, and the field it assigns is published
        // before any continuation can clear it.
        await Task.Yield();

        try
        {
            var token = await issue(policy);

            lock (entry.Gate)
            {
                entry.Token = token;
                entry.InFlight = null;
            }

            return token;
        }
        catch
        {
            // Clear both so the next caller starts a fresh attempt.
            lock (entry.Gate)
            {
                entry.Token = null;
                entry.InFlight = null;
            }

            throw;
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();

        public InstallationToken? Token { get; set; }

        public Task<InstallationToken>? InFlight { get; set; }
    }
}
