namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// Thrown when the command line is wrong, which is distinct from configuration
/// being wrong and exits with a different status.
/// </summary>
public sealed class BrokerUsageException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was expected on the command line.</param>
    public BrokerUsageException(string message)
        : base(message)
    {
    }
}
