using System.Text.Json.Serialization;
using KnightOwl.GitHubTokenBroker.Infrastructure.GitHub;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;

/// <summary>
/// Source-generated serialization for GitHub's REST API.
/// </summary>
/// <remarks>
/// Unmapped members are ignored here, unlike the operator configuration and the
/// client contract: GitHub adds response fields over time, and this service
/// validates the fields it depends on explicitly.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(InstallationTokenRequest))]
[JsonSerializable(typeof(InstallationTokenResponse))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
