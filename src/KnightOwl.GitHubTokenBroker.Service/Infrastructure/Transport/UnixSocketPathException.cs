namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Thrown when the socket directory is unsafe or could not be created, or the
/// socket path could not be freed or bound. The configuration is valid; what needs
/// fixing is on the host.
/// </summary>
/// <param name="message">What is wrong, naming the directory or path.</param>
/// <param name="innerException">The filesystem or socket failure being classified, if any.</param>
public sealed class UnixSocketPathException(string message, Exception? innerException = null)
    : Exception(message, innerException);
