namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// A classified mint failure. The message is a server-side diagnostic and never
/// contains a token, an App JWT, or key material.
/// </summary>
/// <param name="failure">What went wrong, in terms the API and cache act on.</param>
/// <param name="message">Server-side detail, free of secret material.</param>
/// <param name="innerException">The transport or parse error being classified.</param>
public sealed class TokenIssuanceException(
    TokenIssuanceFailure failure,
    string message,
    Exception? innerException = null
)
    : Exception(message, innerException)
{
    /// <summary>How the failure should be handled.</summary>
    public TokenIssuanceFailure Failure { get; } = failure;
}
