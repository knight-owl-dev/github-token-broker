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

    /// <summary>GitHub was unreachable, or answered with a status it may recover from.</summary>
    Unavailable = 7,

    /// <summary>
    /// The private key could not be loaded for this mint, although it loaded at
    /// startup. The file has been replaced with something unusable.
    /// </summary>
    PrivateKeyUnusable = 8,

    /// <summary>The attempt reached its own timeout before GitHub answered.</summary>
    TimedOut = 9,

    /// <summary>GitHub is rate limiting this App.</summary>
    RateLimited = 10,

    /// <summary>
    /// GitHub, or whatever answered in its place, returned a status this broker has
    /// no reading for. Nothing about it says it clears on its own.
    /// </summary>
    UnrecognizedStatus = 11,
}
