using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Git;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;


namespace KnightOwl.GitHubTokenBroker.Cli.Application;

/// <summary>
/// Serves Git's credential-helper protocol.
/// </summary>
/// <remarks>
/// <para>
/// Declining and failing are deliberately different. Git treats a helper that
/// returns nothing as having no credential and carries on unauthenticated, which is
/// exactly right for a repository this broker does not serve: fetching a public
/// upstream must keep working. A broker that cannot be reached is not the same
/// situation, so it is reported and exits non-zero rather than degrading a private
/// repository into a confusing anonymous failure.
/// </para>
/// <para>
/// Nothing read from or written to Git is logged.
/// </para>
/// </remarks>
public sealed class CredentialHelper
{
    /// <summary>The only operation that produces a credential.</summary>
    private const string GetOperation = "get";

    private readonly IBrokerClient _broker;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>Creates the helper.</summary>
    /// <param name="broker">Supplies tokens.</param>
    /// <param name="input">Where Git writes the credential description.</param>
    /// <param name="output">Where the credential is written.</param>
    /// <param name="error">Where diagnostics are written.</param>
    public CredentialHelper(
        IBrokerClient broker,
        TextReader input,
        TextWriter output,
        TextWriter error
    )
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _broker = broker;
        _input = input;
        _output = output;
        _error = error;
    }

    /// <summary>Handles one credential-helper invocation.</summary>
    /// <param name="operation">The Git operation, such as <c>get</c>.</param>
    /// <param name="cancellationToken">Abandons the broker request.</param>
    /// <returns>The process exit status.</returns>
    public async Task<int> RunAsync(string? operation, CancellationToken cancellationToken)
    {
        GitCredentialRequest request;
        try
        {
            // Read first so Git's write always completes, whatever the operation.
            request = GitCredentialProtocol.Read(_input);
        }
        catch (InvalidDataException exception)
        {
            DiagnosticReport.Write(_error, "github-token", exception);
            return CliExitCode.Usage;
        }

        // store and erase have nothing to persist: the token lives for an hour and is
        // never written anywhere. Answering them without minting is the protocol's
        // expected behavior for a read-only helper.
        if (!string.Equals(operation, GetOperation, StringComparison.Ordinal))
        {
            return CliExitCode.Success;
        }

        var resolution = GitCredentialProtocol.TryResolveRepository(
            request,
            out var repository,
            out var resolveError
        );

        if (resolution == GitCredentialResolution.Unusable)
        {
            _error.WriteLine($"github-token: {resolveError}");
            return CliExitCode.Success;
        }

        if (resolution != GitCredentialResolution.Served || repository is null)
        {
            // Addressed to another host or protocol. Git asks broadly, so saying
            // nothing keeps unrelated fetches quiet.
            return CliExitCode.Success;
        }

        try
        {
            var token = await _broker
                .RequestTokenAsync(repository, cancellationToken);

            GitCredentialProtocol.WriteCredential(_output, token.Token);
            return CliExitCode.Success;
        }
        catch (BrokerClientException exception)
            when (exception.Failure == BrokerClientFailure.Refused)
        {
            // Not allowlisted. Decline quietly so Git can proceed anonymously.
            return CliExitCode.Success;
        }
        catch (InvalidDataException exception)
        {
            // Nothing has reached Git yet, so failing is cleaner than handing it a
            // partial credential.
            DiagnosticReport.Write(_error, "github-token", exception);
            return CliExitCode.Internal;
        }
        catch (BrokerClientException exception)
        {
            DiagnosticReport.Write(_error, "github-token", exception);
            return exception.Failure switch
            {
                BrokerClientFailure.Unavailable => CliExitCode.Unavailable,
                BrokerClientFailure.Misconfigured => CliExitCode.Configuration,
                _ => CliExitCode.Internal,
            };
        }
    }
}
