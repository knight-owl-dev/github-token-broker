namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// Process exit statuses, following the conventional <c>sysexits</c> values so a
/// service manager can distinguish an operator mistake from a runtime fault.
/// </summary>
public static class BrokerExitCode
{
    /// <summary>Shut down normally.</summary>
    public const int Success = 0;

    /// <summary>The command line was wrong.</summary>
    public const int Usage = 64;

    /// <summary>
    /// The private key was missing, unreadable, or not a usable RSA key.
    /// </summary>
    /// <remarks>
    /// Its own status because it is the one startup failure an operator fixes
    /// somewhere other than the configuration file.
    /// </remarks>
    public const int PrivateKey = 66;

    /// <summary>
    /// The socket was bound but could not be given its configured mode.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Configuration"/> because the configuration was
    /// valid and the runtime failed.
    /// </remarks>
    public const int SocketMode = 71;

    /// <summary>Startup failed for a reason the broker does not classify.</summary>
    /// <remarks>
    /// Every other status names something to go and fix; this one only says the
    /// broker did not get far enough to know.
    /// </remarks>
    public const int Internal = 70;

    /// <summary>Configuration was missing or invalid, including a listener that could not bind.</summary>
    public const int Configuration = 78;
}
