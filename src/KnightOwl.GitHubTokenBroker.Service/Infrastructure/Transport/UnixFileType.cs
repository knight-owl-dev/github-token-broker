using System.Buffers.Binary;
using System.Runtime.InteropServices;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>Reads the type of a file, which .NET does not report.</summary>
/// <remarks>
/// The type sits in the low bits of <c>st_mode</c>, at an offset into <c>struct stat</c> that differs
/// by platform. Only the runtimes the broker publishes are mapped; elsewhere the answer is
/// <see cref="UnixFileKind.Unknown"/>, which is never grounds to remove a file.
/// </remarks>
internal static partial class UnixFileType
{
    // Larger than struct stat on every mapped platform.
    private const int StatSize = 256;

    private const ushort TypeMask = 0xF000;
    private const ushort SocketType = 0xC000;
    private const ushort SymbolicLinkType = 0xA000;

    private const int NoSuchFile = 2;

    /// <summary>Reports what the path names, without following a link.</summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The kind of file at the path.</returns>
    public static UnixFileKind Of(string path)
    {
        if (ModeOffset() is not { } offset)
        {
            return UnixFileKind.Unknown;
        }

        var stat = new byte[StatSize];
        if (LStat(path, stat) != 0)
        {
            return Marshal.GetLastPInvokeError() == NoSuchFile
                ? UnixFileKind.Missing
                : UnixFileKind.Unknown;
        }

        // Little-endian on every mapped platform, so the type bits are in the
        // first two bytes whether st_mode is 16 or 32 bits wide.
        var mode = BinaryPrimitives.ReadUInt16LittleEndian(stat.AsSpan(offset));

        return (mode & TypeMask) switch
        {
            SocketType => UnixFileKind.Socket,
            SymbolicLinkType => UnixFileKind.SymbolicLink,
            _ => UnixFileKind.Other,
        };
    }

    /// <summary>Where <c>st_mode</c> sits in this platform's <c>struct stat</c>.</summary>
    /// <returns>The byte offset, or <see langword="null"/> on an unmapped platform.</returns>
    private static int? ModeOffset()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;

        if (OperatingSystem.IsLinux())
        {
            // x86-64 puts st_nlink before st_mode; the generic layout arm64 uses does not.
            return architecture switch
            {
                Architecture.X64 => 24,
                Architecture.Arm64 => 16,
                _ => null,
            };
        }

        // After a 32-bit st_dev, with the 64-bit inode layout arm64 always uses.
        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64)
        {
            return 4;
        }

        return null;
    }

    // System locations only, so a libc beside the binary is never the one loaded.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("libc", EntryPoint = "lstat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LStat(string path, [Out] byte[] stat);
}
