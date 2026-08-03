namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;

/// <summary>
/// The part of the client/broker contract that outlives any one version: the
/// liveness route, request limits, and the timeouts each side needs from the other.
/// Versioned routes and bodies live under <c>Contracts.V1</c>.
/// </summary>
public static class BrokerProtocol
{
    /// <summary>Liveness route. Never mints.</summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Largest accepted request body. A request is two short strings, so this is
    /// generous while still bounding an untrusted local caller.
    /// </summary>
    public const int MaxRequestBytes = 4096;

    /// <summary>
    /// Whatever of the client's wait is not the mint: framing the request, reading the
    /// response, and the client's own startup. Named for the wait it comes out of
    /// rather than the listener carrying it, which need not stay a Unix socket.
    /// </summary>
    private static readonly TimeSpan ClientOverhead = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a client waits. Generous enough to cover a cold mint against GitHub,
    /// short enough that a wedged broker fails a Git operation rather than hanging it.
    /// </summary>
    public static readonly TimeSpan ClientRequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long one attempt against GitHub may take, so a hung request cannot hold a
    /// coalesced mint open indefinitely.
    /// </summary>
    public static readonly TimeSpan MintAttemptTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the broker may keep starting fresh attempts at a mint that failed.
    /// </summary>
    /// <remarks>
    /// Subtraction rather than a number someone chose. An attempt begun at the last
    /// permitted moment still runs its whole <see cref="MintAttemptTimeout"/>, so what
    /// a retry may spend is whatever is left of the client's wait once that and the
    /// overhead are set aside. Retuning <see cref="ClientRequestTimeout"/> carries this
    /// with it, where an assertion would only report the breach.
    /// <para>
    /// Computed on read rather than stored, so no declaration order can leave it
    /// subtracting a field that has not been initialized yet.
    /// </para>
    /// </remarks>
    public static TimeSpan MintRetryBudget => ClientRequestTimeout - MintAttemptTimeout - ClientOverhead;
}
