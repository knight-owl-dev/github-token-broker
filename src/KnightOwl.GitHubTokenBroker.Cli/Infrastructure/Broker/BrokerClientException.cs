namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;

/// <summary>A broker request failed, classified so callers can exit accordingly.</summary>
public sealed class BrokerClientException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="failure">How the request failed.</param>
    /// <param name="message">Operator-facing detail, free of secret material.</param>
    public BrokerClientException(BrokerClientFailure failure, string message)
        : base(message)
        => this.Failure = failure;

    /// <summary>Creates the exception from an underlying transport error.</summary>
    /// <param name="failure">How the request failed.</param>
    /// <param name="message">Operator-facing detail, free of secret material.</param>
    /// <param name="innerException">The transport or parse error being classified.</param>
    public BrokerClientException(BrokerClientFailure failure, string message, Exception innerException)
        : base(message, innerException)
        => this.Failure = failure;

    /// <summary>How the request failed.</summary>
    public BrokerClientFailure Failure { get; }
}
