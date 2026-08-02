namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// Thrown when the command line is wrong, which is distinct from configuration
/// being wrong and exits with a different status.
/// </summary>
/// <param name="message">What was expected on the command line.</param>
public sealed class BrokerUsageException(string message) : Exception(message);
