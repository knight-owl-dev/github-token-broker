using System.Diagnostics.CodeAnalysis;
using System.Globalization;


namespace KnightOwl.GitHubTokenBroker.Domain.Access;

/// <summary>One installation of the broker's GitHub App.</summary>
public sealed class InstallationId : IEquatable<InstallationId>
{
    private InstallationId(long value)
    {
        this.Value = value;
        this.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The number GitHub addresses this installation by.</summary>
    public long Value { get; }

    /// <summary>Invariant decimal form, for API routes and cache keys.</summary>
    public string Text { get; }

    /// <summary>Accepts an installation number from configuration.</summary>
    /// <param name="value">Candidate installation number.</param>
    /// <param name="installation">The installation when the value is usable.</param>
    /// <param name="error">Why the value was refused, when it is not.</param>
    /// <returns><see langword="true"/> when the value addresses an installation.</returns>
    public static bool TryCreate(
        long value,
        [NotNullWhen(true)] out InstallationId? installation,
        [NotNullWhen(false)] out string? error
    )
    {
        if (value <= 0)
        {
            installation = null;
            error = "must be a positive number";
            return false;
        }

        installation = new InstallationId(value);
        error = null;
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(InstallationId? other)
        => other is not null && this.Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => Equals(obj as InstallationId);

    /// <inheritdoc/>
    public override int GetHashCode()
        => this.Value.GetHashCode();

    /// <inheritdoc/>
    public override string ToString()
        => this.Text;
}
