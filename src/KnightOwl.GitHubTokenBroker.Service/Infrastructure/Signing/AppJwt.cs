namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// A signed GitHub App JWT. It authenticates the App itself, never a repository,
/// and is never returned through the broker API.
/// </summary>
/// <param name="Value">The compact JWT. Secret.</param>
/// <param name="KeyId">Identifier of the key that signed it. Safe to log.</param>
public sealed record AppJwt(string Value, string KeyId)
{
    /// <inheritdoc/>
    public override string ToString()
        => $"AppJwt(key_id={this.KeyId}, value=redacted)";
}
