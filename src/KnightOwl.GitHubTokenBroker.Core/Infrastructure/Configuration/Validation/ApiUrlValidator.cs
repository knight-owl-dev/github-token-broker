using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>Validates <c>api_url</c>.</summary>
internal static class ApiUrlValidator
{
    /// <summary>Resolves the GitHub API base, defaulting when unconfigured.</summary>
    /// <param name="configured">The configured value, or <see langword="null"/>.</param>
    /// <param name="apiBaseUri">The base address, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when the value is usable.</returns>
    internal static bool TryValidate(
        string? configured,
        [NotNullWhen(true)] out Uri? apiBaseUri,
        [NotNullWhen(false)] out string? error
    )
    {
        apiBaseUri = null;

        if (!Uri.TryCreate(
                configured ?? BrokerConfiguration.DefaultApiUrl,
                UriKind.Absolute,
                out var uri
            ))
        {
            error = "The api_url value must be an absolute URL.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The api_url value must not carry a query or fragment.";
            return false;
        }

        // HTTPS in every real deployment. Plaintext is permitted only against
        // loopback, the controlled seam a local fake GitHub uses in tests.
        var isHttps = uri.Scheme == Uri.UriSchemeHttps;
        var isLoopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            error = "The api_url value must use https, or http only against loopback.";
            return false;
        }

        apiBaseUri = uri;
        error = null;
        return true;
    }
}
