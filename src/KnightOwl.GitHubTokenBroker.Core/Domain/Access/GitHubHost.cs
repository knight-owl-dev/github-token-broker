using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Domain.Access;

/// <summary>
/// A GitHub host this broker will serve. Only <c>github.com</c> is accepted;
/// host expansion is a deliberate later decision and never agent input.
/// </summary>
public sealed class GitHubHost : IEquatable<GitHubHost>
{
    /// <summary>The single accepted host name.</summary>
    public const string GitHubComName = "github.com";

    /// <summary>The only host instance this type can produce.</summary>
    public static readonly GitHubHost GitHubCom = new(GitHubComName);

    private GitHubHost(string name)
        => this.Name = name;

    /// <summary>The host name, as it appears in a clone URL.</summary>
    public string Name { get; }

    /// <summary>
    /// Accepts a host name from configuration or an untrusted client request.
    /// </summary>
    /// <param name="value">Candidate host name, compared exactly.</param>
    /// <param name="host">The accepted host when parsing succeeds.</param>
    /// <param name="error">Why the value was refused, when parsing fails.</param>
    /// <returns><see langword="true"/> when the value names a supported host.</returns>
    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out GitHubHost? host,
        [NotNullWhen(false)] out string? error
    )
    {
        if (string.Equals(value, GitHubComName, StringComparison.Ordinal))
        {
            host = GitHubCom;
            error = null;
            return true;
        }

        host = null;
        error = $"unsupported GitHub host, expected \"{GitHubComName}\"";
        return false;
    }

    /// <inheritdoc/>
    public bool Equals(GitHubHost? other)
        => other is not null && string.Equals(this.Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => Equals(obj as GitHubHost);

    /// <inheritdoc/>
    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(this.Name);

    /// <inheritdoc/>
    public override string ToString()
        => this.Name;
}
