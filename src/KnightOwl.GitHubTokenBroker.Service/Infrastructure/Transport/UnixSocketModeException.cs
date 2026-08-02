namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Thrown when a bound socket could not be given its configured mode. The broker
/// stops rather than serve on a socket whose permissions are unknown, since the
/// mode is who may mint.
/// </summary>
/// <param name="message">What could not be applied, naming the socket and the mode.</param>
/// <param name="innerException">The filesystem failure being classified.</param>
public sealed class UnixSocketModeException(string message, Exception innerException)
    : Exception(message, innerException);
