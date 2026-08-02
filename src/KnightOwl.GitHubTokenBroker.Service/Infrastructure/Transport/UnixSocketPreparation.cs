using System.Net.Sockets;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Confirms a Unix socket path is free to bind. Removes nothing.
/// </summary>
/// <remarks>
/// Clearing an orphaned socket would mean proving the path holds one, and a
/// refused connection does not: Linux answers
/// <see cref="SocketError.ConnectionRefused"/> for an ordinary file as readily as
/// for a socket nobody is listening on, so a mistyped path would cost whatever it
/// names. A connection that succeeds is conclusive, so a broker already running
/// is named as such; anything else is reported for the operator to clear.
/// </remarks>
public static class UnixSocketPreparation
{
    /// <summary>
    /// Mode for a socket directory this method creates. Traversal is the outer half
    /// of the socket's authorization, and it also covers the moment between Kestrel
    /// binding and the mode being applied to the socket itself.
    /// </summary>
    private const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Confirms the path is free to bind, or throws explaining what holds it.</summary>
    /// <param name="socketPath">Absolute path the broker intends to bind.</param>
    /// <exception cref="ConfigurationException">
    /// The directory could not be made safe, or something already occupies the path.
    /// </exception>
    public static void Prepare(string socketPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);

        var parent = Path.GetDirectoryName(socketPath);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ConfigurationException($"The socket path \"{socketPath}\" names no directory.");
        }

        PrepareDirectory(parent);

        if (Directory.Exists(socketPath))
        {
            throw new ConfigurationException($"The socket path \"{socketPath}\" is a directory.");
        }

        if (!File.Exists(socketPath))
        {
            return;
        }

        // Checked before connecting: a link's target is a separate file that must not
        // be reached through this path at all.
        if (File.ResolveLinkTarget(socketPath, returnFinalTarget: false) is not null)
        {
            throw new ConfigurationException($"The socket path \"{socketPath}\" is a symbolic link.");
        }

        if (IsListening(socketPath))
        {
            throw new ConfigurationException($"Another process is already listening on \"{socketPath}\".");
        }

        throw new ConfigurationException($"The socket path \"{socketPath}\" is occupied. Remove it once nothing is using it.");
    }

    /// <summary>
    /// Creates the socket directory owner-only, or refuses one that already exists
    /// and lets another account replace what the broker binds.
    /// </summary>
    /// <param name="parent">The directory holding the socket.</param>
    private static void PrepareDirectory(string parent)
    {
        if (!Directory.Exists(parent))
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(parent);
                }
                else
                {
                    Directory.CreateDirectory(parent, DirectoryMode);
                }
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ConfigurationException(
                    $"The socket directory \"{parent}\" could not be created.",
                    exception
                );
            }

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Write on the directory is the right to unlink the socket and bind another
        // in its place, so it is refused even when the socket's own mode is narrow.
        // Traversal for a group stays available through 0750.
        var mode = new DirectoryInfo(parent).UnixFileMode;
        var writableByOthers =
            mode.HasFlag(UnixFileMode.GroupWrite) || mode.HasFlag(UnixFileMode.OtherWrite);

        // Unless the sticky bit is set, which is what makes a shared directory like
        // /tmp safe: everyone may create, only the owner may unlink.
        if (writableByOthers && !mode.HasFlag(UnixFileMode.StickyBit))
        {
            throw new ConfigurationException($"The socket directory \"{parent}\" is writable beyond its owner and is not sticky (mode {Format(mode)}).");
        }
    }

    /// <summary>Renders a mode as the octal an operator wrote in configuration.</summary>
    /// <param name="mode">The mode to render.</param>
    /// <returns>Four octal digits.</returns>
    internal static string Format(UnixFileMode mode)
        => Convert.ToString((int) mode, 8).PadLeft(4, '0');

    /// <summary>Reports whether a process is accepting connections on the path.</summary>
    /// <param name="socketPath">The occupied path.</param>
    /// <returns><see langword="true"/> when the connection succeeded.</returns>
    private static bool IsListening(string socketPath)
    {
        using Socket probe = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
