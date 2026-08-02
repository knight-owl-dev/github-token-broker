namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

internal sealed class TcpDocument
{
    public string? Address { get; init; }

    public int? Port { get; init; }

    public string? ClientCredentialPath { get; init; }
}
