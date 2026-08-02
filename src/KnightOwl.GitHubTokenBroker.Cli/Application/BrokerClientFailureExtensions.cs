using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;


namespace KnightOwl.GitHubTokenBroker.Cli.Application;

/// <summary>Turns a broker failure into the status this process exits with.</summary>
internal static class BrokerClientFailureExtensions
{
    extension(BrokerClientFailure failure)
    {
        /// <summary>The exit status this failure produces.</summary>
        public int ExitCode
            => failure switch
            {
                BrokerClientFailure.Unavailable => CliExitCode.Unavailable,
                BrokerClientFailure.Refused => CliExitCode.NotAuthorized,
                BrokerClientFailure.Failed => CliExitCode.Internal,
                BrokerClientFailure.Misconfigured => CliExitCode.Configuration,
            };
    }
}
