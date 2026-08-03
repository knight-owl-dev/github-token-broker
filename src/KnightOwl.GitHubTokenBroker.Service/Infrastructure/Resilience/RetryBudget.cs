namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Resilience;

/// <summary>
/// Runs an operation again while a time budget allows it.
/// </summary>
/// <remarks>
/// Bounded by elapsed time rather than a count of attempts, since a count says
/// nothing about how long the attempts took. An attempt already under way is never
/// interrupted: the budget decides only whether another one starts, so the worst
/// case is the budget plus one whole attempt.
/// </remarks>
internal sealed class RetryBudget
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _budget;
    private readonly TimeSpan _delay;

    /// <summary>Creates a budget.</summary>
    /// <param name="timeProvider">The monotonic clock elapsed time is read from.</param>
    /// <param name="budget">How long fresh attempts may keep starting.</param>
    /// <param name="delay">How long to wait between attempts.</param>
    public RetryBudget(TimeProvider timeProvider, TimeSpan budget, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        _timeProvider = timeProvider;
        _budget = budget;
        _delay = delay;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it succeeds, until it fails in a way
    /// <paramref name="isWorthRetrying"/> rejects, or until the budget is spent.
    /// </summary>
    /// <typeparam name="TResult">What a successful attempt produces.</typeparam>
    /// <typeparam name="TFailure">The exception type worth examining.</typeparam>
    /// <param name="attempt">The operation to run.</param>
    /// <param name="isWorthRetrying">Decides whether a failure earns another attempt.</param>
    /// <param name="cancellationToken">Abandons the wait between attempts.</param>
    /// <returns>What the first successful attempt produced.</returns>
    /// <remarks>
    /// The last failure is what propagates, so a caller still learns why the whole
    /// thing failed rather than that it ran out of time.
    /// </remarks>
    public async Task<TResult> RunAsync<TResult, TFailure>(
        Func<CancellationToken, Task<TResult>> attempt,
        Func<TFailure, bool> isWorthRetrying,
        CancellationToken cancellationToken
    )
        where TFailure : Exception
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(isWorthRetrying);

        // Monotonic: a wall clock stepped by NTP would either spend the budget at once
        // or never spend it, and the second leaves this loop unbounded.
        var startedAt = _timeProvider.GetTimestamp();

        while (true)
        {
            // Checked here rather than left to the wait, which a zero delay skips.
            // Without it, a loop configured with no delay ignores cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await attempt(cancellationToken);
            }
            catch (TFailure failure) when (isWorthRetrying(failure))
            {
                // The wait is spent from the same budget as the attempts, so there has
                // to be room for it and for what it precedes.
                if (_timeProvider.GetElapsedTime(startedAt) + _delay >= _budget)
                {
                    throw;
                }
            }

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, _timeProvider, cancellationToken);
            }
        }
    }
}
