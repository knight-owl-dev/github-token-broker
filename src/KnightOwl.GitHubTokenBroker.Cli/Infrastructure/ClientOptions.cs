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
    /// <summary>Names the broker endpoint, in <c>unix://</c> form.</summary>
    public const string EndpointVariable = "GITHUB_TOKEN_BROKER_ENDPOINT";

    /// <summary>Absolute path to the real GitHub CLI, when it should not be searched for.</summary>
    public const string GitHubCliVariable = "GITHUB_TOKEN_BROKER_GH";

    private ClientOptions(BrokerEndpoint endpoint, string? gitHubCliPath)
    {
        this.Endpoint = endpoint;
        this.GitHubCliPath = gitHubCliPath;
    }

    /// <summary>Where the broker is.</summary>
    public BrokerEndpoint Endpoint { get; }

    /// <summary>An explicit GitHub CLI path, or <see langword="null"/> to search.</summary>
    public string? GitHubCliPath { get; }

    /// <summary>Reads options from environment variables.</summary>
    /// <param name="read">Resolves an environment variable by name.</param>
    /// <param name="options">The options when they are complete and valid.</param>
    /// <param name="error">What is missing or malformed, when they are not.</param>
    /// <returns><see langword="true"/> when the client is configured.</returns>
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

        var gitHubCliPath = read(GitHubCliVariable);
        options = new ClientOptions(
            endpoint,
            string.IsNullOrWhiteSpace(gitHubCliPath) ? null : gitHubCliPath
        );

        error = null;
        return true;
    }
}
