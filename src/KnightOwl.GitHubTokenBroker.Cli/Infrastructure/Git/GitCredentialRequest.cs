namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Git;

/// <summary>
/// The fields Git supplies to a credential helper that this client acts on.
/// </summary>
/// <param name="Protocol">Transport Git is using, which must be <c>https</c>.</param>
/// <param name="Host">Host Git is authenticating to.</param>
/// <param name="Path">
/// Repository path, present only when <c>credential.useHttpPath</c> is enabled. It
/// is what makes a credential specific to one repository.
/// </param>
public sealed record GitCredentialRequest(string? Protocol, string? Host, string? Path);
