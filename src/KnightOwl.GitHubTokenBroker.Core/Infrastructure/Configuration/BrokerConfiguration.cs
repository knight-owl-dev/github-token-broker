using System.Text.Json;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;

/// <summary>
/// Validated broker configuration, and the adapter that turns an operator's JSON
/// file into domain objects. Construction is only possible through
/// <see cref="Load"/> or <see cref="FromJson"/>, so an instance is always
/// internally consistent and its allowlist is always non-empty.
/// </summary>
/// <remarks>
/// Each section is checked by its own validator under
/// <c>Infrastructure.Configuration.Validation</c>.
/// </remarks>
public sealed class BrokerConfiguration
{
    /// <summary>Default API base. Overriding it is a controlled testing seam.</summary>
    public const string DefaultApiUrl = "https://api.github.com";

    /// <summary>
    /// Default freshness margin: a cached token is reused only while more than
    /// this remains before expiry.
    /// </summary>
    public static readonly TimeSpan DefaultRefreshMargin = TimeSpan.FromMinutes(5);

    private BrokerConfiguration(
        GitHubHost host,
        Uri apiBaseUri,
        long appId,
        long installationId,
        string privateKeyPath,
        TimeSpan refreshMargin,
        ListenOptions listen,
        RepositoryAllowlist allowlist
    )
    {
        this.Host = host;
        this.ApiBaseUri = apiBaseUri;
        this.AppId = appId;
        this.InstallationId = installationId;
        this.PrivateKeyPath = privateKeyPath;
        this.RefreshMargin = refreshMargin;
        this.Listen = listen;
        this.Allowlist = allowlist;
    }

    /// <summary>The GitHub host this broker serves.</summary>
    public GitHubHost Host { get; }

    /// <summary>Base address for GitHub API calls.</summary>
    public Uri ApiBaseUri { get; }

    /// <summary>The GitHub App identity used as the JWT issuer.</summary>
    public long AppId { get; }

    /// <summary>The installation whose tokens this broker mints.</summary>
    public long InstallationId { get; }

    /// <summary>Absolute path to the App private key, read only by the broker.</summary>
    public string PrivateKeyPath { get; }

    /// <summary>How much life a cached token must have left to be reused.</summary>
    public TimeSpan RefreshMargin { get; }

    /// <summary>The local transports the broker accepts requests on.</summary>
    public ListenOptions Listen { get; }

    /// <summary>The repositories this broker will mint for, and their ceilings.</summary>
    public RepositoryAllowlist Allowlist { get; }

    /// <summary>Reads and validates configuration from an absolute file path.</summary>
    /// <param name="configurationPath">Absolute path to the operator-owned JSON file.</param>
    /// <returns>Validated configuration.</returns>
    /// <exception cref="ConfigurationException">The file is unreadable or invalid.</exception>
    public static BrokerConfiguration Load(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configurationPath);

        if (!Path.IsPathRooted(configurationPath))
        {
            throw new ConfigurationException("The configuration path must be absolute.");
        }

        string json;
        try
        {
            json = File.ReadAllText(configurationPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException(
                $"The configuration could not be read from \"{configurationPath}\".",
                exception
            );
        }

        return FromJson(json);
    }

    /// <summary>Validates configuration already held as JSON text.</summary>
    /// <param name="json">The configuration document.</param>
    /// <returns>Validated configuration.</returns>
    /// <exception cref="ConfigurationException">The document is invalid for this schema.</exception>
    public static BrokerConfiguration FromJson(string json)
    {
        ConfigurationDocument document;
        try
        {
            document = JsonSerializer.Deserialize(json, ConfigurationJsonContext.Default.ConfigurationDocument)
                ?? throw new ConfigurationException("The configuration is empty.");
        }
        catch (JsonException exception)
        {
            // The serializer message names the member or position, never a value.
            throw new ConfigurationException(
                "The configuration does not match this schema.",
                exception
            );
        }

        return Validate(document);
    }

    private static BrokerConfiguration Validate(ConfigurationDocument document)
    {
        if (!IdentityValidator.TryValidate(document, out var identity, out var error))
        {
            throw new ConfigurationException(error);
        }

        if (!ApiUrlValidator.TryValidate(document.ApiUrl, out var apiBaseUri, out error))
        {
            throw new ConfigurationException(error);
        }

        if (!RefreshMarginValidator.TryValidate(
                document.TokenRefreshMarginSeconds,
                out var refreshMargin,
                out error
            ))
        {
            throw new ConfigurationException(error);
        }

        if (!ListenValidator.TryValidate(document.Listen, out var listen, out error))
        {
            throw new ConfigurationException(error);
        }

        if (!AllowlistValidator.TryValidate(document.Repositories, out var allowlist, out error))
        {
            throw new ConfigurationException(error);
        }

        return new BrokerConfiguration(
            identity.Host,
            apiBaseUri,
            identity.AppId,
            identity.InstallationId,
            identity.PrivateKeyPath,
            refreshMargin,
            listen,
            allowlist
        );
    }
}
