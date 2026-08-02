using System.Text.Json.Serialization;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;

/// <summary>
/// Source-generated serialization for the operator configuration file.
/// </summary>
/// <remarks>
/// Generated rather than reflected so the broker can be published ahead of time.
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> is the security-relevant
/// setting: an unrecognized key fails startup instead of being ignored.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    AllowTrailingCommas = false
)]
[JsonSerializable(typeof(ConfigurationDocument))]
internal sealed partial class ConfigurationJsonContext : JsonSerializerContext;
