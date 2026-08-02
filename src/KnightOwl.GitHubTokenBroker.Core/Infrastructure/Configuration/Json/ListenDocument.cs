namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

internal sealed class ListenDocument
{
    public string? UnixSocket { get; init; }

    public string? UnixSocketMode { get; init; }

    public TcpDocument? Tcp { get; init; }
}
