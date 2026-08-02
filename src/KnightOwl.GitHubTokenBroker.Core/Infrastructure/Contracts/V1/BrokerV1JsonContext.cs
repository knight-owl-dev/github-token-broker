using System.Text.Json.Serialization;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;

/// <summary>
/// Source-generated serialization for the version 1 bodies, shared by both sides
/// so a message cannot be written one way and read another.
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
[JsonSerializable(typeof(BrokerRepositoryRequest))]
[JsonSerializable(typeof(BrokerTokenResponse))]
[JsonSerializable(typeof(BrokerCheckResponse))]
public sealed partial class BrokerV1JsonContext : JsonSerializerContext;
