using System.Net;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>
/// Explicitly configured local TCP listener. The client credential authorizes
/// access to the broker's already restricted allowlist; it expands no GitHub
/// permission and is not the App private key.
/// </summary>
/// <param name="Address">The address to bind.</param>
/// <param name="Port">The port to bind.</param>
/// <param name="ClientCredentialPath">Absolute path to the shared credential file.</param>
public sealed record TcpListenerOptions(
    IPAddress Address,
    int Port,
    string ClientCredentialPath
);
