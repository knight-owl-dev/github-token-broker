using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// Fails every read the way <see cref="FilePrivateKeySource"/> does when the file
/// has been replaced with something unusable since startup.
/// </summary>
internal sealed class UnreadablePrivateKeySource : IPrivateKeySource
{
    public PrivateKeyMaterial Load()
        => throw new ConfigurationException("The private key could not be read from \"/absent.pem\".");
}
