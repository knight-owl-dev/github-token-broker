namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>
/// A Unix socket listener and the mode its socket file is given.
/// </summary>
/// <remarks>
/// Connecting to a Unix socket needs write permission on the socket file, so the
/// mode is the whole of this transport's authorization. Kestrel leaves it at the
/// process umask, which is inherited and invisible, so the broker sets it.
/// </remarks>
/// <param name="Path">Absolute path the broker binds.</param>
/// <param name="Mode">Mode applied to the socket once it exists.</param>
public sealed record UnixSocketOptions(string Path, UnixFileMode Mode);
