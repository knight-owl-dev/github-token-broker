namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

/// <summary>
/// Wire shape of the operator configuration file. Every member is nullable so
/// validation, rather than deserialization, produces the diagnostic. Unmapped
/// members are rejected by the serializer options, so an unrecognized
/// security-sensitive key fails startup instead of being ignored.
/// </summary>
internal sealed class ConfigurationDocument
{
    public string? GithubHost { get; init; }

    public string? ApiUrl { get; init; }

    public long? AppId { get; init; }

    public long? InstallationId { get; init; }

    public string? PrivateKeyPath { get; init; }

    public int? TokenRefreshMarginSeconds { get; init; }

    public ListenDocument? Listen { get; init; }

    public Dictionary<string, RepositoryDocument>? Repositories { get; init; }
}
