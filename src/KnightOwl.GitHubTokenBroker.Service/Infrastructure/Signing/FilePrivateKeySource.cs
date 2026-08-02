using System.Security.Cryptography;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// Reads the App private key from an operator-owned PEM file.
/// </summary>
/// <remarks>
/// The key is re-read on every mint. Mints are rare because tokens are cached for
/// most of their hour, so the cost is negligible, and it buys two properties worth
/// more than the saving: replacing the file rotates the key with no restart, and no
/// shared <see cref="RSA"/> instance can be disposed while another mint is using it.
/// </remarks>
public sealed class FilePrivateKeySource : IPrivateKeySource
{
    private readonly string _privateKeyPath;

    /// <summary>Binds the source to a PEM file path.</summary>
    /// <param name="privateKeyPath">Absolute path to the App private key.</param>
    public FilePrivateKeySource(string privateKeyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyPath);
        _privateKeyPath = privateKeyPath;
    }

    /// <inheritdoc/>
    /// <exception cref="ConfigurationException">
    /// The file is missing, unreadable, or does not contain an RSA private key.
    /// Diagnostics name the path and never quote file contents.
    /// </exception>
    public PrivateKeyMaterial Load()
    {
        string pem;
        try
        {
            pem = File.ReadAllText(_privateKeyPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException(
                $"The private key could not be read from \"{_privateKeyPath}\".",
                exception
            );
        }

        RSA key = RSA.Create();
        try
        {
            // Handles the PKCS#1 "RSA PRIVATE KEY" that GitHub issues, and PKCS#8.
            key.ImportFromPem(pem);
        }
        catch (ArgumentException exception)
        {
            key.Dispose();
            throw new ConfigurationException(
                $"The private key at \"{_privateKeyPath}\" is not a PEM-encoded RSA key.",
                exception
            );
        }
        catch (CryptographicException exception)
        {
            key.Dispose();
            throw new ConfigurationException(
                $"The private key at \"{_privateKeyPath}\" could not be imported.",
                exception
            );
        }

        try
        {
            return new PrivateKeyMaterial(key, ComputeKeyId(key));
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Derives a stable identifier from the public half, so it can be logged and
    /// compared across a rotation without exposing the private key.
    /// </summary>
    /// <param name="key">The loaded key pair.</param>
    /// <returns>A short hex digest of the public half.</returns>
    private static string ComputeKeyId(RSA key)
    {
        var publicKey = key.ExportRSAPublicKey();
        var digest = SHA256.HashData(publicKey);
        CryptographicOperations.ZeroMemory(publicKey);
        return Convert.ToHexStringLower(digest)[..16];
    }
}
