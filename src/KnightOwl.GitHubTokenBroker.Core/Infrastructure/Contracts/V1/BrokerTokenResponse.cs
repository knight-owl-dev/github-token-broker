namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;

/// <summary>
/// Body of a successful <c>POST /v1/token</c>. The only broker response carrying
/// secret material, returned solely to a local client that is about to hand it to
/// Git or the GitHub CLI.
/// </summary>
public sealed record BrokerTokenResponse
{
    /// <summary>Opaque installation token. No prefix or length is assumed.</summary>
    public required string Token { get; init; }

    /// <summary>When the token stops working, as reported by GitHub.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// The record's own would print the token, and one interpolation is all it
    /// would take.
    /// </remarks>
    public override string ToString()
        => $"{nameof(BrokerTokenResponse)} {{ ExpiresAt = {this.ExpiresAt:O} }}";
}
