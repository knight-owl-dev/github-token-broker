using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;


namespace KnightOwl.GitHubTokenBroker.Cli.Application;

/// <summary>
/// Reports whether the broker is reachable and will serve a repository.
/// </summary>
/// <remarks>
/// No token is requested, so running this proves authorization without consuming a
/// mint or putting a secret anywhere.
/// </remarks>
public sealed class AuthorizationCheck
{
    private readonly IBrokerClient _broker;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>Creates the check.</summary>
    /// <param name="broker">The broker to interrogate.</param>
    /// <param name="output">Where the result is written.</param>
    /// <param name="error">Where diagnostics are written.</param>
    public AuthorizationCheck(IBrokerClient broker, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _broker = broker;
        _output = output;
        _error = error;
    }

    /// <summary>Checks one repository.</summary>
    /// <param name="repository">The repository to check.</param>
    /// <param name="cancellationToken">Abandons the broker request.</param>
    /// <returns>The process exit status.</returns>
    public async Task<int> RunAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(repository);

        try
        {
            var response = await _broker.CheckAsync(repository, cancellationToken);

            await _output.WriteLineAsync($"{response.Repository} {response.Permissions}");
            return CliExitCode.Success;
        }
        catch (BrokerClientException exception)
        {
            await DiagnosticReport.WriteAsync(_error, Globals.AppName, exception);
            return exception.Failure switch
            {
                BrokerClientFailure.Unavailable => CliExitCode.Unavailable,
                BrokerClientFailure.Refused => CliExitCode.NotAuthorized,
                BrokerClientFailure.Misconfigured => CliExitCode.Configuration,
                _ => CliExitCode.Internal,
            };
        }
    }
}
