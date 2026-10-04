using System.Net.Sockets;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>Frees a Unix socket path for the broker to bind.</summary>
/// <remarks>
/// A refused connection cannot prove a path holds a dead socket: Linux answers
/// <see cref="SocketError.ConnectionRefused"/> for an ordinary file as readily as for a socket nobody is
/// listening on. So the file's type is read first, and only a socket that refuses a connection is removed;
/// anything else is refused, so a mistyped path never deletes what it names.
/// </remarks>
public static class UnixSocketPreparation
{
    /// <summary>
    /// Mode for a socket directory this method creates. Traversal is the outer half
    /// of the socket's authorization.
    /// </summary>
    private const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Frees the path to bind, or throws explaining what holds it.</summary>
    /// <param name="socketPath">Absolute path the broker intends to bind.</param>
    /// <returns>Whether the path was free, or held a dead socket that was removed.</returns>
    /// <exception cref="ConfigurationException">The socket path names no directory.</exception>
    /// <exception cref="UnixSocketPathException">
    /// The directory could not be made safe, or something other than a dead socket occupies the path.
    /// </exception>
    public static UnixSocketPathState Prepare(string socketPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(socketPath);

        var parent = Path.GetDirectoryName(socketPath);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ConfigurationException($"The socket path \"{socketPath}\" names no directory.");
        }

        PrepareDirectory(parent);

        return ClearPath(socketPath);
    }

    /// <summary>
    /// Frees the path, removing a dead socket, or throws explaining what holds it.
    /// </summary>
    /// <param name="socketPath">The path to free.</param>
    /// <returns>Whether the path was free, or held a dead socket that was removed.</returns>
    private static UnixSocketPathState ClearPath(string socketPath)
    {
        if (!Path.Exists(socketPath))
        {
            return UnixSocketPathState.Free;
        }

        return UnixFileType.Of(socketPath) switch
        {
            UnixFileKind.Missing => UnixSocketPathState.Free,

            UnixFileKind.SymbolicLink
                => throw new UnixSocketPathException($"The socket path \"{socketPath}\" is a symbolic link, which the broker does not follow."),

            UnixFileKind.Socket => RemoveIfDead(socketPath),

            UnixFileKind.Other when Directory.Exists(socketPath)
                => throw new UnixSocketPathException($"The socket path \"{socketPath}\" is a directory."),

            UnixFileKind.Other
                => throw new UnixSocketPathException($"The socket path \"{socketPath}\" holds a file that is not a socket."),

            UnixFileKind.Unknown
                => throw new UnixSocketPathException($"The type of the file at \"{socketPath}\" could not be read."),
        };
    }

    /// <summary>Removes a socket nothing answers on.</summary>
    /// <param name="socketPath">The dead socket.</param>
    private static void RemoveDeadSocket(string socketPath)
    {
        try
        {
            File.Delete(socketPath);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UnixSocketPathException(
                $"The dead socket at \"{socketPath}\" could not be removed.",
                exception
            );
        }
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
                throw new UnixSocketPathException(
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
        //
        // The owner is not checked: .NET exposes none, and UnixFileType reads only the type.
        // What that leaves open is in github-token-broker(8).
        var mode = new DirectoryInfo(parent).UnixFileMode;
        var writableByOthers =
            mode.HasFlag(UnixFileMode.GroupWrite) || mode.HasFlag(UnixFileMode.OtherWrite);

        // Unless the sticky bit is set, which is what makes a shared directory like
        // /tmp safe: everyone may create, only the owner may unlink.
        if (writableByOthers && !mode.HasFlag(UnixFileMode.StickyBit))
        {
            throw new UnixSocketPathException($"The socket directory \"{parent}\" is writable beyond its owner and is not sticky (mode {Format(mode)}).");
        }
    }

    /// <summary>Renders a mode as the octal an operator wrote in configuration.</summary>
    /// <param name="mode">The mode to render.</param>
    /// <returns>Four octal digits.</returns>
    internal static string Format(UnixFileMode mode)
        => Convert.ToString((int) mode, 8).PadLeft(4, '0');

    /// <summary>
    /// Removes a socket that refuses a connection, or throws explaining why it stays.
    /// </summary>
    /// <param name="socketPath">The socket at the path.</param>
    /// <returns>Free when the socket vanished before the probe, reclaimed once removed.</returns>
    private static UnixSocketPathState RemoveIfDead(string socketPath)
    {
        using Socket probe = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException) when (UnixFileType.Of(socketPath) == UnixFileKind.Missing)
        {
            // Unlinked since its type was read, by a broker stopping cleanly.
            return UnixSocketPathState.Free;
        }
        catch (SocketException exception)
            when (exception.SocketErrorCode == SocketError.ConnectionRefused)
        {
            RemoveDeadSocket(socketPath);
            return UnixSocketPathState.Reclaimed;
        }
        catch (SocketException exception)
        {
            // Denied or busy, which a live socket answers too.
            throw new UnixSocketPathException(
                $"The socket at \"{socketPath}\" could not be probed, so it is left in place.",
                exception
            );
        }

        throw new UnixSocketPathException($"Another process is already listening on \"{socketPath}\".");
    }
}
