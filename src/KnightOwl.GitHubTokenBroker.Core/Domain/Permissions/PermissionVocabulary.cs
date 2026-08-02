using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Domain.Permissions;

/// <summary>
/// The closed permission vocabulary this broker will name in a token request.
/// </summary>
public static class PermissionVocabulary
{
    /// <summary>
    /// Permissions the broker may request. A name outside this set fails
    /// configuration validation rather than reaching GitHub.
    /// </summary>
    public static readonly FrozenSet<string> Supported =
        new[] { "metadata", "contents", "pull_requests", "issues", "actions", "checks", "statuses" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Refused at any level, with a diagnostic distinct from "unsupported".
    /// Workflow-file authority is outside this design, so it is unrepresentable
    /// rather than merely unset.
    /// </summary>
    public static readonly FrozenSet<string> Forbidden =
        new[] { "workflows" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// GitHub grants metadata read implicitly and reports it on every
    /// installation token, requested or not, so grant validation tolerates it
    /// even when configuration omits it.
    /// </summary>
    public const string ImplicitMetadata = "metadata";

    /// <summary>Parses a wire level name such as <c>read</c> or <c>write</c>.</summary>
    /// <param name="value">Candidate level name.</param>
    /// <param name="level">The parsed level when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the value names a supported level.</returns>
    public static bool TryParseLevel(
        string? value,
        [NotNullWhen(true)] out PermissionLevel? level
    )
    {
        switch (value)
        {
            case "read":
                level = PermissionLevel.Read;
                return true;
            case "write":
                level = PermissionLevel.Write;
                return true;
            default:
                level = null;
                return false;
        }
    }

    /// <summary>Renders a level as the name GitHub expects on the wire.</summary>
    /// <param name="level">The level to render.</param>
    /// <returns>The wire name for the level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The level is not a known value.</exception>
    public static string ToWireValue(this PermissionLevel level)
        => level switch
        {
            PermissionLevel.Read => "read",
            PermissionLevel.Write => "write",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
}
