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

    /// <summary>Configuration or the private key was missing or invalid.</summary>
    public const int Configuration = 78;
}
