namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// A classified mint failure. The message is a server-side diagnostic and never
/// contains a token, an App JWT, or key material.
/// </summary>
public sealed class TokenIssuanceException : Exception
{
    /// <summary>Creates a classified failure.</summary>
    /// <param name="failure">What went wrong, in terms the API and cache act on.</param>
    /// <param name="message">Server-side detail, free of secret material.</param>
    public TokenIssuanceException(TokenIssuanceFailure failure, string message)
        : base(message)
        => this.Failure = failure;

    /// <summary>Creates a classified failure from an underlying error.</summary>
    /// <param name="failure">What went wrong, in terms the API and cache act on.</param>
    /// <param name="message">Server-side detail, free of secret material.</param>
    /// <param name="innerException">The transport or parse error being classified.</param>
    public TokenIssuanceException(TokenIssuanceFailure failure, string message, Exception innerException)
        : base(message, innerException)
        => this.Failure = failure;

    /// <summary>How the failure should be handled.</summary>
    public TokenIssuanceFailure Failure { get; }
}
