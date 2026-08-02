using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Domain.Repositories;

/// <summary>
/// A validated <c>OWNER/REPOSITORY</c> pair, and the only repository identity
/// used by configuration, allowlist lookups, token requests, and cache keys.
/// </summary>
/// <remarks>
/// GitHub compares owner and repository names case-insensitively while
/// preserving their display casing. <see cref="Key"/> therefore drives every
/// comparison and <see cref="FullName"/> carries the configured casing into API
/// requests. Input is never trimmed or otherwise repaired: a value that is not
/// already canonical is rejected, so no two spellings can collapse onto one
/// identity.
/// </remarks>
public sealed class RepositoryName : IEquatable<RepositoryName>
{
    private const int MaxOwnerLength = 39;
    private const int MaxRepositoryLength = 100;

    private RepositoryName(string owner, string name)
    {
        this.Owner = owner;
        this.Name = name;
        this.FullName = string.Concat(owner, "/", name);
        this.Key = this.FullName.ToLowerInvariant();
    }

    /// <summary>The account or organization that owns the repository.</summary>
    public string Owner { get; }

    /// <summary>The repository name without its owner.</summary>
    public string Name { get; }

    /// <summary>The value as configured, used when calling GitHub.</summary>
    public string FullName { get; }

    /// <summary>Case-folded identity for comparison and cache keys.</summary>
    public string Key { get; }

    /// <summary>Parses a repository name, throwing when it is not canonical.</summary>
    /// <param name="value">Candidate <c>OWNER/REPOSITORY</c> value.</param>
    /// <returns>The parsed repository name.</returns>
    /// <exception cref="FormatException">The value is not a canonical repository name.</exception>
    public static RepositoryName Parse(string? value)
        => TryParse(value, out var repository, out var error) ? repository : throw new FormatException(error);

    /// <summary>Parses a repository name without throwing.</summary>
    /// <param name="value">Candidate <c>OWNER/REPOSITORY</c> value.</param>
    /// <param name="repository">The parsed name when parsing succeeds.</param>
    /// <param name="error">Why the value was refused, when parsing fails.</param>
    /// <returns><see langword="true"/> when the value is a canonical repository name.</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out RepositoryName? repository,
        [NotNullWhen(false)] out string? error
    )
    {
        repository = null;

        if (string.IsNullOrEmpty(value))
        {
            error = "repository is empty";
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                error = "repository contains whitespace";
                return false;
            }

            if (!char.IsControl(character))
            {
                continue;
            }

            error = "repository contains a control character";
            return false;
        }

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator < 0)
        {
            error = "repository is not in OWNER/REPOSITORY form";
            return false;
        }

        if (value.IndexOf('/', separator + 1) >= 0)
        {
            error = "repository has more than one path separator";
            return false;
        }

        var owner = value[..separator];
        var name = value[(separator + 1)..];

        if (!IsValidOwner(owner))
        {
            error = "repository owner is not a valid GitHub account name";
            return false;
        }

        if (!IsValidRepository(name))
        {
            error = "repository name is not a valid GitHub repository name";
            return false;
        }

        repository = new RepositoryName(owner, name);
        error = null;
        return true;
    }

    private static bool IsValidOwner(string owner)
    {
        if (owner.Length is 0 or > MaxOwnerLength)
        {
            return false;
        }

        if (owner[0] == '-' || owner[^1] == '-')
        {
            return false;
        }

        return owner.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static bool IsValidRepository(string name)
    {
        if (name.Length is 0 or > MaxRepositoryLength)
        {
            return false;
        }

        // "." and ".." are traversal, never repository names. A leading dot is
        // otherwise legitimate, as in ".github".
        if (name is "." or "..")
        {
            return false;
        }

        // GitHub rejects a ".git" suffix at creation time. Callers deriving a
        // name from a clone URL strip it before parsing.
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.'
        );
    }

    /// <inheritdoc/>
    public bool Equals(RepositoryName? other)
        => other is not null && string.Equals(this.Key, other.Key, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => Equals(obj as RepositoryName);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(this.Key);

    /// <inheritdoc/>
    public override string ToString()
        => this.FullName;
}
