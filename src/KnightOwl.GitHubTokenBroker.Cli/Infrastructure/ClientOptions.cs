using System.Diagnostics.CodeAnalysis;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure;

/// <summary>
/// Everything the client needs, read from the environment. The client holds no
/// configuration file and never learns the App identity, the private key path, or
/// the allowlist.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>Names the broker endpoint, in <c>unix://</c> or <c>http://</c> form.</summary>
    public const string EndpointVariable = "GITHUB_TOKEN_BROKER_ENDPOINT";

    /// <summary>Path to a file holding the TCP client credential.</summary>
    public const string CredentialFileVariable = "GITHUB_TOKEN_BROKER_CREDENTIAL_FILE";

    /// <summary>The TCP client credential itself.</summary>
    public const string CredentialVariable = "GITHUB_TOKEN_BROKER_CREDENTIAL";

    /// <summary>Absolute path to the real GitHub CLI, when it should not be searched for.</summary>
    public const string GitHubCliVariable = "GITHUB_TOKEN_BROKER_GH";

    private ClientOptions(
        BrokerEndpoint endpoint,
        string? clientCredential,
        string? gitHubCliPath
    )
    {
        this.Endpoint = endpoint;
        this.ClientCredential = clientCredential;
        this.GitHubCliPath = gitHubCliPath;
    }

    /// <summary>Where the broker is.</summary>
    public BrokerEndpoint Endpoint { get; }

    /// <summary>The TCP credential, or <see langword="null"/> when none was supplied.</summary>
    public string? ClientCredential { get; }

    /// <summary>An explicit GitHub CLI path, or <see langword="null"/> to search.</summary>
    public string? GitHubCliPath { get; }

    /// <summary>Reads options from environment variables.</summary>
    /// <param name="read">Resolves an environment variable by name.</param>
    /// <param name="options">The options when they are complete and valid.</param>
    /// <param name="error">What is missing or malformed, when they are not.</param>
    /// <returns><see langword="true"/> when the client is configured.</returns>
    /// <remarks>
    /// A credential file is preferred over a credential value: an environment
    /// variable is visible to anything that can read this process's environment,
    /// while a file can be restricted by ownership and mode.
    /// </remarks>
    public static bool TryRead(
        Func<string, string?> read,
        [NotNullWhen(true)] out ClientOptions? options,
        [NotNullWhen(false)] out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(read);

        options = null;

        var rawEndpoint = read(EndpointVariable);
        if (string.IsNullOrWhiteSpace(rawEndpoint))
        {
            error = $"The {EndpointVariable} variable is not set.";
            return false;
        }

        if (!BrokerEndpoint.TryParse(rawEndpoint, out var endpoint, out var endpointError))
        {
            error = $"The {EndpointVariable} variable is invalid: {endpointError}.";
            return false;
        }

        string? credential = null;
        if (read(CredentialFileVariable) is { Length: > 0 } credentialFile)
        {
            try
            {
                credential = File.ReadAllText(credentialFile).Trim();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                error = $"The {CredentialFileVariable} file \"{credentialFile}\" could not be read.";
                return false;
            }
        }
        else if (read(CredentialVariable) is { Length: > 0 } inlineCredential)
        {
            credential = inlineCredential.Trim();
        }

        if (endpoint.Kind == BrokerEndpointKind.Http && string.IsNullOrEmpty(credential))
        {
            error = $"An HTTP endpoint requires {CredentialFileVariable} or {CredentialVariable}.";

            return false;
        }

        var gitHubCliPath = read(GitHubCliVariable);
        options = new ClientOptions(
            endpoint,
            credential,
            string.IsNullOrWhiteSpace(gitHubCliPath) ? null : gitHubCliPath
        );

        error = null;
        return true;
    }
}
