using System.Security.Cryptography;
using System.Text;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// The shared secret a TCP client must present.
/// </summary>
/// <remarks>
/// This authorizes reaching the broker's already restricted allowlist. It grants no
/// GitHub permission of its own and is not the App private key.
/// </remarks>
public sealed class ClientCredential
{
    /// <summary>
    /// Shortest accepted credential. Long enough that a guess is impractical, since
    /// a local caller can retry without limit.
    /// </summary>
    private const int MinimumLength = 32;

    private readonly byte[] _expected;

    private ClientCredential(byte[] expected)
        => _expected = expected;

    /// <summary>Reads the credential from an operator-owned file.</summary>
    /// <param name="path">Absolute path to the credential file.</param>
    /// <returns>The loaded credential.</returns>
    /// <exception cref="ConfigurationException">
    /// The file is unreadable or holds too little entropy. The value is never logged
    /// or echoed in the message.
    /// </exception>
    public static ClientCredential Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string contents;
        try
        {
            contents = File.ReadAllText(path);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException(
                $"The client credential could not be read from \"{path}\".",
                exception
            );
        }

        // A trailing newline is what every editor and `openssl rand` pipeline leaves
        // behind, so it is stripped rather than treated as part of the secret.
        var value = contents.Trim();
        if (value.Length < MinimumLength)
        {
            throw new ConfigurationException($"The client credential at \"{path}\" must be at least {MinimumLength} characters.");
        }

        return new ClientCredential(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>Compares a presented credential in constant time.</summary>
    /// <param name="presented">Header value supplied by the client.</param>
    /// <returns><see langword="true"/> when the value matches.</returns>
    public bool Matches(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            _expected
        );
    }
}
