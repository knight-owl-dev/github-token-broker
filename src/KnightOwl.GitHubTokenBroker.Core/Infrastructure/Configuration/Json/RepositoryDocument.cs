namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

internal sealed class RepositoryDocument
{
    public Dictionary<string, string>? Permissions { get; init; }
}
