namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// A clock the test moves by hand. Expiry behavior is therefore asserted without
/// any real delay.
/// </summary>
internal sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset start)
        => _now = start;

    public override DateTimeOffset GetUtcNow()
        => _now;

    public void Advance(TimeSpan amount)
        => _now = _now.Add(amount);
}
