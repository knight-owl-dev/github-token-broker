using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Domain.Permissions;

/// <summary>
/// An immutable, canonically ordered permission map. Ordering makes
/// <see cref="CanonicalForm"/> a stable cache-key component.
/// </summary>
public sealed class PermissionSet : IEquatable<PermissionSet>
{
    private readonly ImmutableSortedDictionary<string, PermissionLevel> _permissions;

    private PermissionSet(ImmutableSortedDictionary<string, PermissionLevel> permissions)
    {
        _permissions = permissions;
        this.CanonicalForm = string.Join(
            ';',
            permissions.Select(entry => $"{entry.Key}:{entry.Value.ToWireValue()}")
        );
    }

    /// <summary>
    /// Stable rendering of the whole map, used as a cache-key component and in
    /// equality.
    /// </summary>
    public string CanonicalForm { get; }

    /// <summary>How many permissions the set declares.</summary>
    public int Count => _permissions.Count;

    /// <summary>The permissions in canonical name order.</summary>
    public IEnumerable<KeyValuePair<string, PermissionLevel>> Entries => _permissions;

    /// <summary>
    /// Builds a validated set, rejecting an empty map, a forbidden name, an
    /// unsupported name, an unsupported level, and a duplicate name.
    /// </summary>
    /// <param name="requested">Permission names and levels as written by an operator.</param>
    /// <param name="permissionSet">The set, when creation succeeds.</param>
    /// <param name="error">Why creation was refused, when it fails.</param>
    /// <returns><see langword="true"/> when the map forms a usable set.</returns>
    public static bool TryCreate(
        IEnumerable<KeyValuePair<string, string>> requested,
        [NotNullWhen(true)] out PermissionSet? permissionSet,
        [NotNullWhen(false)] out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(requested);

        permissionSet = null;
        var builder =
            ImmutableSortedDictionary.CreateBuilder<string, PermissionLevel>(StringComparer.Ordinal);

        foreach (var (name, level) in requested)
        {
            if (PermissionVocabulary.Forbidden.Contains(name))
            {
                error = $"permission \"{name}\" is never requested by this broker";
                return false;
            }

            if (!PermissionVocabulary.Supported.Contains(name))
            {
                error = $"unsupported permission \"{name}\"";
                return false;
            }

            if (!PermissionVocabulary.TryParseLevel(level, out var parsed))
            {
                error = $"unsupported level \"{level}\" for permission \"{name}\"";
                return false;
            }

            if (builder.ContainsKey(name))
            {
                error = $"permission \"{name}\" is declared more than once";
                return false;
            }

            builder.Add(name, parsed.Value);
        }

        if (builder.Count == 0)
        {
            error = "permissions must declare at least one entry";
            return false;
        }

        permissionSet = new PermissionSet(builder.ToImmutable());
        error = null;
        return true;
    }

    /// <summary>
    /// True when every granted permission is present here at an equal or lower
    /// level, so the grant claims nothing beyond this ceiling.
    /// </summary>
    /// <param name="granted">The permissions to weigh against this ceiling.</param>
    /// <param name="excess">The first permission that exceeds it, when one does.</param>
    /// <returns><see langword="true"/> when the ceiling covers the grant.</returns>
    public bool Covers(
        IEnumerable<KeyValuePair<string, PermissionLevel>> granted,
        [NotNullWhen(false)] out string? excess
    )
    {
        ArgumentNullException.ThrowIfNull(granted);

        foreach (var (name, level) in granted)
        {
            if (name == PermissionVocabulary.ImplicitMetadata
                && level == PermissionLevel.Read)
            {
                continue;
            }

            if (!_permissions.TryGetValue(name, out var ceiling))
            {
                excess = $"granted unconfigured permission \"{name}\"";
                return false;
            }

            if (level <= ceiling)
            {
                continue;
            }

            excess = $"granted \"{name}\" at {level.ToWireValue()} "
                + $"above configured {ceiling.ToWireValue()}";

            return false;
        }

        excess = null;
        return true;
    }

    /// <summary>Renders the set as the permission map for a token request.</summary>
    /// <returns>Permission names mapped to their wire level names.</returns>
    public Dictionary<string, string> ToWireMap()
        => _permissions.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToWireValue(),
            StringComparer.Ordinal
        );

    /// <inheritdoc/>
    public bool Equals(PermissionSet? other)
        => other is not null
            && string.Equals(this.CanonicalForm, other.CanonicalForm, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => Equals(obj as PermissionSet);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(this.CanonicalForm);

    /// <inheritdoc/>
    public override string ToString()
        => this.CanonicalForm;
}
