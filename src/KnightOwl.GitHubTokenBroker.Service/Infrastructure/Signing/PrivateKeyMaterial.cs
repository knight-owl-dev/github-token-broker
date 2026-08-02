using System.Security.Cryptography;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// A loaded App private key together with a safe identifier for it. Only the
/// broker ever holds this, and it is disposed as soon as one JWT is signed.
/// </summary>
public sealed class PrivateKeyMaterial : IDisposable
{
    /// <summary>Wraps a loaded key and its identifier.</summary>
    /// <param name="key">The signing key.</param>
    /// <param name="keyId">A non-secret identifier derived from the public key.</param>
    public PrivateKeyMaterial(RSA key, string keyId)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrEmpty(keyId);

        this.Key = key;
        this.KeyId = keyId;
    }

    /// <summary>The RSA key used to sign App JWTs.</summary>
    public RSA Key { get; }

    /// <summary>
    /// Hash of the public key, safe to log. It lets an operator confirm which key
    /// a broker is using across a rotation without revealing key material.
    /// </summary>
    public string KeyId { get; }

    /// <inheritdoc/>
    public void Dispose()
        => this.Key.Dispose();
}
