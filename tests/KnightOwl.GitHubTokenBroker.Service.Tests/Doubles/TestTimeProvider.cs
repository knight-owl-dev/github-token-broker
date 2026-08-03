namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// A clock the test moves by hand, on both readings a caller can take: the instant an
/// expiry is compared against, and the monotonic count a duration is measured from.
/// Either is therefore asserted without any real delay.
/// </summary>
/// <param name="start">The instant both readings begin at.</param>
internal sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    private long _timestamp;

    /// <summary>
    /// One tick apiece, so <see cref="Advance"/> moves both readings in the same unit.
    /// </summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
        => _now;

    public override long GetTimestamp()
        => _timestamp;

    public void Advance(TimeSpan amount)
    {
        _now = _now.Add(amount);
        _timestamp += amount.Ticks;
    }
}
