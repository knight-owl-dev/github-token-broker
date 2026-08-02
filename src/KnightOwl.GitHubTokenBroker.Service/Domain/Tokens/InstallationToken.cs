using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Service.Domain.Tokens;

/// <summary>
/// A minted installation token and its expiry. The value is opaque: no prefix,
/// format, or length is assumed, and <see cref="ToString"/> is redacted so a
/// token cannot reach a log through interpolation or an exception message.
/// </summary>
public sealed class InstallationToken
{
    private InstallationToken(string value, DateTimeOffset expiresAt)
    {
        this.Value = value;
        this.ExpiresAt = expiresAt;
    }

    /// <summary>The token itself. Secret, and never logged.</summary>
    public string Value { get; }

    /// <summary>When GitHub stops accepting the token.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Builds a token, rejecting an empty value and an expiry that is missing or
    /// already past at the moment of issue.
    /// </summary>
    /// <param name="value">The token as GitHub returned it.</param>
    /// <param name="expiresAt">The expiry as GitHub reported it.</param>
    /// <param name="issuedAt">Now, as the caller's clock reads it.</param>
    /// <param name="token">The token, when creation succeeds.</param>
    /// <param name="error">Why creation was refused, when it fails.</param>
    /// <returns><see langword="true"/> when the response is usable.</returns>
    public static bool TryCreate(
        string? value,
        DateTimeOffset? expiresAt,
        DateTimeOffset issuedAt,
        [NotNullWhen(true)] out InstallationToken? token,
        [NotNullWhen(false)] out string? error
    )
    {
        token = null;

        if (string.IsNullOrEmpty(value))
        {
            error = "response carried no token";
            return false;
        }

        // A client hands the token to line-based protocols, where an embedded
        // newline ends the value early. Opaque otherwise: no prefix, length, or
        // alphabet is assumed.
        if (value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            error = "response token holds a control character or whitespace";
            return false;
        }

        if (expiresAt is null)
        {
            error = "response carried no expiry";
            return false;
        }

        if (expiresAt.Value <= issuedAt)
        {
            error = "response expiry is not in the future";
            return false;
        }

        token = new InstallationToken(value, expiresAt.Value);
        error = null;
        return true;
    }

    /// <summary>
    /// True while more than <paramref name="margin"/> remains before expiry, which
    /// is the only condition under which a cached token is reused.
    /// </summary>
    /// <param name="now">The moment to judge freshness at.</param>
    /// <param name="margin">How much life the token must have left.</param>
    /// <returns><see langword="true"/> when the token is still fresh.</returns>
    public bool IsFreshAt(DateTimeOffset now, TimeSpan margin)
        => this.ExpiresAt - now > margin;

    /// <inheritdoc/>
    /// <remarks>Redacted, so interpolation cannot leak the token.</remarks>
    public override string ToString()
        => $"InstallationToken(expires_at={this.ExpiresAt:O}, value=redacted)";
}
