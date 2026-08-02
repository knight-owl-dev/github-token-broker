namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>
/// Where the broker listens. At least one form is required; both may run at once
/// so one host can serve a mounted socket and a Docker Desktop client together.
/// </summary>
/// <param name="UnixSocket">The socket listener, or <see langword="null"/>.</param>
/// <param name="Tcp">The TCP listener, or <see langword="null"/>.</param>
public sealed record ListenOptions(UnixSocketOptions? UnixSocket, TcpListenerOptions? Tcp);
