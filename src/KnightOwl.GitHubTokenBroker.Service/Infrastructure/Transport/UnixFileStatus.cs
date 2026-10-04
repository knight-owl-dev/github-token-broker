using System.Buffers.Binary;
using System.Runtime.InteropServices;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>What <c>stat</c> reports about a file that .NET does not: its type and its owner.</summary>
/// <param name="Kind">What the path names.</param>
/// <param name="OwnerId">The owning user's id, or <see langword="null"/> when unknown.</param>
/// <remarks>
/// Both sit in <c>struct stat</c>, at offsets that differ by platform. Only the runtimes the broker
/// publishes are mapped; elsewhere the kind is <see cref="UnixFileKind.Unknown"/>, which is never grounds
/// to remove a file, and the owner is unknown.
/// </remarks>
internal readonly partial record struct UnixFileStatus(UnixFileKind Kind, uint? OwnerId)
{
    // Larger than struct stat on every mapped platform.
    private const int StatSize = 256;

    private const ushort TypeMask = 0xF000;
    private const ushort SocketType = 0xC000;
    private const ushort SymbolicLinkType = 0xA000;

    private const int NoSuchFile = 2;

    /// <summary>Reports on the path itself, a link rather than its target.</summary>
    /// <param name="path">The path to read.</param>
    /// <returns>What the path names, and who owns it.</returns>
    public static UnixFileStatus Of(string path)
        => Read(path, LStat);

    /// <summary>Reports on the file the path resolves to, following a link.</summary>
    /// <param name="path">The path to read.</param>
    /// <returns>What the path resolves to, and who owns it.</returns>
    public static UnixFileStatus OfTarget(string path)
        => Read(path, Stat);

    private static UnixFileStatus Read(string path, Func<string, byte[], int> call)
    {
        if (Layout() is not { } layout)
        {
            return new UnixFileStatus(UnixFileKind.Unknown, null);
        }

        var stat = new byte[StatSize];
        if (call(path, stat) != 0)
        {
            var kind = Marshal.GetLastPInvokeError() == NoSuchFile
                ? UnixFileKind.Missing
                : UnixFileKind.Unknown;

            return new UnixFileStatus(kind, null);
        }

        // Little-endian on every mapped platform, so the type bits are in the
        // first two bytes whether st_mode is 16 or 32 bits wide.
        var mode = BinaryPrimitives.ReadUInt16LittleEndian(stat.AsSpan(layout.Mode));
        var owner = BinaryPrimitives.ReadUInt32LittleEndian(stat.AsSpan(layout.Owner));

        var fileKind = (mode & TypeMask) switch
        {
            SocketType => UnixFileKind.Socket,
            SymbolicLinkType => UnixFileKind.SymbolicLink,
            _ => UnixFileKind.Other,
        };

        return new UnixFileStatus(fileKind, owner);
    }

    /// <summary>Where <c>st_mode</c> and <c>st_uid</c> sit in this platform's <c>struct stat</c>.</summary>
    /// <returns>The byte offsets, or <see langword="null"/> on an unmapped platform.</returns>
    private static (int Mode, int Owner)? Layout()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        if (OperatingSystem.IsLinux())
        {
            // x86-64 puts st_nlink before st_mode; the generic layout arm64 uses puts it after.
            return architecture switch
            {
                Architecture.X64 => (24, 28),
                Architecture.Arm64 => (16, 24),
                _ => null,
            };
        }

        // st_mode after a 32-bit st_dev; st_uid after the 64-bit inode that arm64 always uses.
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64)
        {
            return (4, 16);
        }

        return null;
    }

    // System locations only, so a libc beside the binary is never the one loaded.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LStat(string path, [Out] byte[] stat);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "stat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Stat(string path, [Out] byte[] stat);
}
