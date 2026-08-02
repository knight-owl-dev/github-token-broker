namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>
/// Thrown when operator-owned configuration is missing, malformed, or declares
/// something security-sensitive this broker refuses to honor. Messages name the
/// offending setting and never quote file contents or key material.
/// </summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>Creates the exception with a setting-level diagnostic.</summary>
    /// <param name="message">What is wrong, naming the setting and not its value.</param>
    public ConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception from an underlying parse or IO failure.</summary>
    /// <param name="message">What is wrong, naming the setting and not its value.</param>
    /// <param name="innerException">The failure being classified.</param>
    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
