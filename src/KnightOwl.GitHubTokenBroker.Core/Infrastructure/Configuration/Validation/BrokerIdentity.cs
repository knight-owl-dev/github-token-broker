using KnightOwl.GitHubTokenBroker.Domain.Access;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>
/// The GitHub App this broker acts as, and the host it acts on.
/// </summary>
/// <param name="Host">The GitHub host this broker serves.</param>
/// <param name="AppId">The App identity used as the JWT issuer.</param>
/// <param name="Installation">The installation an allowlist entry falls back to.</param>
/// <param name="PrivateKeyPath">Absolute path to the App private key.</param>
internal sealed record BrokerIdentity(
    GitHubHost Host,
    long AppId,
    InstallationId Installation,
    string PrivateKeyPath
);
