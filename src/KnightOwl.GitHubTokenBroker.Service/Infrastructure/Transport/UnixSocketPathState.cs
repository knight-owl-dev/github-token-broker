namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>What was at the socket path before the broker freed it.</summary>
public enum UnixSocketPathState
{
    /// <summary>Nothing was there.</summary>
    Free,

    /// <summary>A dead socket was there, and was removed.</summary>
    Reclaimed,
}
