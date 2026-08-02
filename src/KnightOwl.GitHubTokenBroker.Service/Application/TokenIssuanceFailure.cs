namespace KnightOwl.GitHubTokenBroker.Service.Application;

/// <summary>
/// Why a mint attempt failed. The API maps these to client-facing status codes,
/// and the application uses them to decide whether a cached entry and the loaded
/// private key must be discarded.
/// </summary>
public enum TokenIssuanceFailure
{
    /// <summary>GitHub rejected the App JWT: clock skew, a revoked key, or a rotated key.</summary>
    AppUnauthorized = 2,

    /// <summary>The installation is suspended or otherwise forbidden.</summary>
    InstallationForbidden = 3,

    /// <summary>The installation or the requested repository is no longer present.</summary>
    InstallationOrRepositoryMissing = 4,

    /// <summary>
    /// GitHub refused the requested permission map, which means broker
    /// configuration has drifted from what the App registration actually grants.
    /// </summary>
    PermissionDrift = 5,

    /// <summary>The response did not satisfy the guarantees this broker requires.</summary>
    UntrustworthyResponse = 6,

    /// <summary>GitHub was unreachable, timed out, or returned an unexpected status.</summary>
    Unavailable = 7,
}
