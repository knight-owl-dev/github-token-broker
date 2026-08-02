namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>Source-generated log messages emitted once at startup.</summary>
internal static partial class BrokerStartupLog
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
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Listening on {SocketPath} with mode {SocketMode}"
    )]
    public static partial void UnixSocketReady(ILogger logger, string socketPath, string socketMode);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Critical,
        Message = "The socket {SocketPath} could not be given mode {SocketMode}; shutting down rather than serving on a socket whose permissions are unknown"
    )]
    public static partial void UnixSocketModeFailed(
        ILogger logger,
        string socketPath,
        string socketMode,
        Exception exception
    );
}
