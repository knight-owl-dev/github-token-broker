using System.Text.Json.Serialization;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;

/// <summary>
/// Source-generated serialization for the bodies that belong to no one version.
/// </summary>
/// <remarks>
/// Generated rather than reflected so both executables can be published ahead of
/// time. Unmapped members are rejected, making an unexpected field an error rather
/// than something silently dropped.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    MaxDepth = 8,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Disallow,
    AllowTrailingCommas = false
)]
[JsonSerializable(typeof(BrokerHealthResponse))]
[JsonSerializable(typeof(BrokerErrorResponse))]
public sealed partial class BrokerJsonContext : JsonSerializerContext;
