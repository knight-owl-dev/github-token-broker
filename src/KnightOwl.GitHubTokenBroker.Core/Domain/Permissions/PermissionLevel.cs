namespace KnightOwl.GitHubTokenBroker.Domain.Permissions;

/// <summary>
/// Access level for one repository permission. Ordered so a comparison expresses
/// "no more than the configured ceiling".
/// </summary>
public enum PermissionLevel
{
    /// <summary>Read-only access.</summary>
    Read = 1,

    /// <summary>Read and write access, which subsumes <see cref="Read"/>.</summary>
    Write = 2,
}
