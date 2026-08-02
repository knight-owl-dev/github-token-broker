using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Narrows the process umask for a scope, restoring it on dispose.
/// </summary>
/// <remarks>
/// Binding creates the socket file, and its mode then comes from the umask
/// rather than from configuration — an inherited <c>002</c> leaves it
/// group-writable, which on a socket is the right to connect. That stands until
/// <c>NarrowUnixSocket</c> runs. Narrowing the umask has the socket born
/// owner-only instead, so every widening is one of the operator asked for.
/// </remarks>
internal sealed class RestrictedUmask : IDisposable
{
    /// <summary>Octal 0177, so a socket is created 0600.</summary>
    private const uint OwnerOnly = 0b111_1111;

    private readonly uint _previous;
    private readonly bool _applied;

    private RestrictedUmask(uint previous, bool applied)
    {
        _previous = previous;
        _applied = applied;
    }

    /// <summary>Narrows the umask until the returned scope is disposed.</summary>
    /// <returns>A scope that restores the previous umask.</returns>
    /// <remarks>
    /// Does nothing on Windows, which has no umask and where the mode this
    /// protects is not applied either.
    /// </remarks>
    public static RestrictedUmask Apply()
        => OperatingSystem.IsWindows()
            ? new RestrictedUmask(0, applied: false)
            : new RestrictedUmask(Umask(OwnerOnly), applied: true);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_applied)
        {
            _ = Umask(_previous);
        }
    }

    // DllImport rather than LibraryImport: the signature is blittable, so the
    // generator would buy nothing and its generated marshaling would put
    // AllowUnsafeBlocks on the whole project.
    [SuppressMessage(
        "Interoperability",
        "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute to generate P/Invoke marshalling code at compile time",
        Justification = "See above."
    )]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "umask")]
    private static extern uint Umask(uint mask);
}
