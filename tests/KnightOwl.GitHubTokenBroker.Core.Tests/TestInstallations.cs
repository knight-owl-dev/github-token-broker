using KnightOwl.GitHubTokenBroker.Domain.Access;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

/// <summary>Builds installation identities for tests.</summary>
internal static class TestInstallations
{
    public const long DefaultNumber = 789012;

    public static InstallationId Installation(long value = DefaultNumber)
        => InstallationId.TryCreate(value, out var installation, out var error)
            ? installation
            : throw new InvalidOperationException(error);
}
