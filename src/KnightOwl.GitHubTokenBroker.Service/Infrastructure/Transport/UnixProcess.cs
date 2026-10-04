using System.Runtime.InteropServices;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>What libc reports about this process that .NET does not.</summary>
internal static partial class UnixProcess
{
    /// <summary>The user id this process acts as.</summary>
    public static uint EffectiveUserId
        => GetEffectiveUserId();

    // System locations only, so a libc beside the binary is never the one loaded.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
