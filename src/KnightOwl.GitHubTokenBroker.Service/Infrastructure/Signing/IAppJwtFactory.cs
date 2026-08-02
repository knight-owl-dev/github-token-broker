namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// Creates short-lived App JWTs for authenticating as the GitHub App.
/// </summary>
public interface IAppJwtFactory
{
    /// <summary>Signs a JWT valid for the next few minutes.</summary>
    /// <returns>The signed JWT and the identifier of the signing key.</returns>
    AppJwt Create();
}
