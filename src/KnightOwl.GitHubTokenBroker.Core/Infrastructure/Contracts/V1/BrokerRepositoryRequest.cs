namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;

/// <summary>
/// Body of <c>POST /v1/token</c> and <c>POST /v1/check</c>. Both members are
/// untrusted client input and are validated by the broker before use.
/// </summary>
public sealed record BrokerRepositoryRequest
{
    /// <summary>The GitHub host the client is asking about.</summary>
    public string? Host { get; init; }

    /// <summary>The requested repository in <c>OWNER/REPOSITORY</c> form.</summary>
    public string? Repository { get; init; }
}
