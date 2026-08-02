namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

internal sealed class UnixSocketDocument
{
    public string? Path { get; init; }

    public string? Mode { get; init; }
}
