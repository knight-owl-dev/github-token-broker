using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;


namespace KnightOwl.GitHubTokenBroker.Cli.Application;

/// <summary>
/// Runs the real GitHub CLI authenticated for one repository.
/// </summary>
/// <remarks>
/// The token exists only in the child's environment block. It is never written to
/// <c>hosts.yml</c>, passed as an argument, or persisted by <c>gh auth login</c>, so
/// it disappears when the child exits.
/// </remarks>
public sealed class GitHubCliRunner
{
    /// <summary>Where the GitHub CLI reads a token from the environment.</summary>
    private const string GhTokenVariable = "GH_TOKEN";

    /// <summary>An older token variable, removed so it cannot take precedence.</summary>
    private const string GitHubTokenVariable = "GITHUB_TOKEN";

    /// <summary>Pins the child to the repository the token is scoped to.</summary>
    private const string GhRepositoryVariable = "GH_REPO";

    private readonly IBrokerClient _broker;
    private readonly IProcessLauncher _launcher;
    private readonly Func<string, string?> _readEnvironment;
    private readonly string? _configuredGitHubCliPath;
    private readonly string? _ownExecutablePath;
    private readonly TextWriter _error;

    /// <summary>Creates the runner.</summary>
    /// <param name="broker">Supplies the token.</param>
    /// <param name="launcher">Starts the child process.</param>
    /// <param name="readEnvironment">Resolves an environment variable by name.</param>
    /// <param name="configuredGitHubCliPath">Explicit GitHub CLI path, when configured.</param>
    /// <param name="ownExecutablePath">This executable, excluded from the search.</param>
    /// <param name="error">Where diagnostics are written.</param>
    public GitHubCliRunner(
        IBrokerClient broker,
        IProcessLauncher launcher,
        Func<string, string?> readEnvironment,
        string? configuredGitHubCliPath,
        string? ownExecutablePath,
        TextWriter error
    )
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(readEnvironment);
        ArgumentNullException.ThrowIfNull(error);

        _broker = broker;
        _launcher = launcher;
        _readEnvironment = readEnvironment;
        _configuredGitHubCliPath = configuredGitHubCliPath;
        _ownExecutablePath = ownExecutablePath;
        _error = error;
    }

    /// <summary>Obtains a token and runs the GitHub CLI with it.</summary>
    /// <param name="repository">The repository to authenticate for.</param>
    /// <param name="arguments">Arguments for the GitHub CLI.</param>
    /// <param name="cancellationToken">Abandons the broker request.</param>
    /// <returns>
    /// The child's exit status when it ran, otherwise a status describing why it
    /// could not be started.
    /// </returns>
    public async Task<int> RunAsync(
        RepositoryName repository,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!GitHubCliLocator.TryLocate(
                _configuredGitHubCliPath,
                _readEnvironment("PATH"),
                _ownExecutablePath,
                out var executablePath,
                out var locateError
            ))
        {
            _error.WriteLine($"github-token: {locateError}");
            return CliExitCode.Usage;
        }

        BrokerTokenResponse token;
        try
        {
            token = await _broker
                .RequestTokenAsync(repository, cancellationToken);
        }
        catch (BrokerClientException exception)
        {
            DiagnosticReport.Write(_error, "github-token", exception);
            return exception.Failure switch
            {
                BrokerClientFailure.Unavailable => CliExitCode.Unavailable,
                BrokerClientFailure.Refused => CliExitCode.NotAuthorized,
                BrokerClientFailure.Misconfigured => CliExitCode.Configuration,
                _ => CliExitCode.Internal,
            };
        }

        return _launcher.Run(
            executablePath,
            arguments,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [GhTokenVariable] = token.Token,
                [GitHubTokenVariable] = null,
                [GhRepositoryVariable] = repository.FullName,
            }
        );
    }
}
