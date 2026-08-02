namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>Where the broker listens.</summary>
/// <param name="UnixSocket">The socket listener.</param>
/// <remarks>
/// One transport, so who may mint is a filesystem question with one answer.
/// Still a record, since it mirrors the <c>listen</c> section, which grows a
/// member if a cross-host transport is ever added.
/// </remarks>
public sealed record ListenOptions(UnixSocketOptions UnixSocket);
