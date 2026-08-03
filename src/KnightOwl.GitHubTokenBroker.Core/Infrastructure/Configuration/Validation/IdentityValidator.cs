using System.Diagnostics.CodeAnalysis;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>
/// Validates <c>github_host</c>, <c>app_id</c>, <c>installation_id</c>, and
/// <c>private_key_path</c>.
/// </summary>
internal static class IdentityValidator
{
    /// <summary>Resolves the App identity this broker mints as.</summary>
    /// <param name="document">The deserialized configuration.</param>
    /// <param name="identity">The identity, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when every member is usable.</returns>
    internal static bool TryValidate(
        ConfigurationDocument document,
        [NotNullWhen(true)] out BrokerIdentity? identity,
        [NotNullWhen(false)] out string? error
    )
    {
        identity = null;

        if (!GitHubHost.TryParse(
                document.GithubHost ?? GitHubHost.GitHubComName,
                out var host,
                out var hostError
            ))
        {
            error = $"The github_host value is invalid: {hostError}.";
            return false;
        }

        if (document.AppId is not { } appId)
        {
            error = "The app_id value is required.";
            return false;
        }

        if (appId <= 0)
        {
            error = "The app_id value must be a positive number.";
            return false;
        }

        if (document.InstallationId is not { } configured)
        {
            error = "The installation_id value is required.";
            return false;
        }

        if (!InstallationId.TryCreate(configured, out var installation, out var installationError))
        {
            error = $"The installation_id value {installationError}.";
            return false;
        }

        if (document.PrivateKeyPath is not { } privateKeyPath)
        {
            error = "The private_key_path value is required.";
            return false;
        }

        if (!Path.IsPathRooted(privateKeyPath))
        {
            error = "The private_key_path value must be absolute.";
            return false;
        }

        identity = new BrokerIdentity(host, appId, installation, privateKeyPath);
        error = null;
        return true;
    }
}
