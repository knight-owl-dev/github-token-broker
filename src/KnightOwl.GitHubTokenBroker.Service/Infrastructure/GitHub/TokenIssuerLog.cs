namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;

/// <summary>
/// Source-generated log messages for token minting. The key identifier is a hash of
/// the public key, so it is safe to record.
/// </summary>
internal static partial class TokenIssuerLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Minted a token for {Repository} at {Permissions} using key {KeyId}, expiring {ExpiresAt:O}"
    )]
    public static partial void Minted(
        ILogger logger,
        string repository,
        string permissions,
        string keyId,
        DateTimeOffset expiresAt
    );
}
