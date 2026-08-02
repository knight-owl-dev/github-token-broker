namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// Source-generated log messages the host emits: the broker is serving, or it
/// cannot and is stopping. Anything a request produces belongs to its own module.
/// </summary>
internal static partial class BrokerHostLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Broker ready for app {AppId} installation {InstallationId} with {Repositories} allowlisted repositories"
    )]
    public static partial void Ready(
        ILogger logger,
        long appId,
        long installationId,
        int repositories
    );

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Private key {KeyId} loaded"
    )]
    public static partial void PrivateKeyLoaded(ILogger logger, string keyId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Listening on {SocketPath} with mode {SocketMode}"
    )]
    public static partial void UnixSocketReady(ILogger logger, string socketPath, string socketMode);

}
