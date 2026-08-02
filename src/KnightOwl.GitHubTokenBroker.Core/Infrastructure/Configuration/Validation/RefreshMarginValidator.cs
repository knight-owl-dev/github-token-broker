using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>Validates <c>token_refresh_margin_seconds</c>.</summary>
internal static class RefreshMarginValidator
{
    /// <summary>Resolves the freshness margin, defaulting when unconfigured.</summary>
    /// <param name="configuredSeconds">The configured value, or <see langword="null"/>.</param>
    /// <param name="refreshMargin">The margin, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when the value is usable.</returns>
    internal static bool TryValidate(
        int? configuredSeconds,
        out TimeSpan refreshMargin,
        [NotNullWhen(false)] out string? error
    )
    {
        refreshMargin = BrokerConfiguration.DefaultRefreshMargin;

        if (configuredSeconds is null)
        {
            error = null;
            return true;
        }

        // An installation token lives one hour, so a margin approaching that would
        // make every token stale on arrival.
        if (configuredSeconds is < 30 or > 1800)
        {
            error = "The token_refresh_margin_seconds value must be between 30 and 1800.";
            return false;
        }

        refreshMargin = TimeSpan.FromSeconds(configuredSeconds.Value);
        error = null;
        return true;
    }
}
