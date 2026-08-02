using KnightOwl.GitHubTokenBroker.Service.Application;


namespace KnightOwl.GitHubTokenBroker.Service.Api;

/// <summary>
/// Source-generated log messages for the API surface. Templates are fixed at
/// compile time, which keeps a token or credential from being interpolated into a
/// message by accident.
/// </summary>
internal static partial class BrokerApiLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Rejected a TCP request without a valid client credential"
    )]
    public static partial void CredentialRejected(ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Refused a repository request: {Reason}"
    )]
    public static partial void RequestRefused(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Could not mint a token for {Repository}: {Failure}: {Reason}"
    )]
    public static partial void MintFailed(
        ILogger logger,
        string repository,
        TokenIssuanceFailure failure,
        string reason
    );
}
