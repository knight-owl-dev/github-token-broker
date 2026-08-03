namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

internal sealed class RepositoryDocument
{
    public long? InstallationId { get; init; }

    public Dictionary<string, string>? Permissions { get; init; }
}
