using System.Security.Cryptography;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>Supplies a fixed in-memory key, counting how often it was read.</summary>
internal sealed class StubPrivateKeySource : IPrivateKeySource
{
    private readonly string _pem;

    public StubPrivateKeySource(RSA key)
        => _pem = TestKeys.ToPkcs1Pem(key);

    public int LoadCount { get; private set; }

    public PrivateKeyMaterial Load()
    {
        this.LoadCount++;
        RSA key = RSA.Create();
        key.ImportFromPem(_pem);
        return new PrivateKeyMaterial(key, "test-key");
    }
}
