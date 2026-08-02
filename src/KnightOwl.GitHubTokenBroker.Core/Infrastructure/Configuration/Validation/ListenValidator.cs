using System.Diagnostics.CodeAnalysis;
using System.Text;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>Validates <c>listen</c>.</summary>
internal static class ListenValidator
{
    /// <summary>
    /// Mode given to the socket when the operator names none. Owner only, because
    /// widening it is what grants another account the right to mint.
    /// </summary>
    internal const UnixFileMode DefaultSocketMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Resolves the transport the broker will accept requests on.</summary>
    /// <param name="listen">The configured listener, or <see langword="null"/>.</param>
    /// <param name="options">The listener options, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when a usable transport is configured.</returns>
    internal static bool TryValidate(
        ListenDocument? listen,
        [NotNullWhen(true)] out ListenOptions? options,
        [NotNullWhen(false)] out string? error
    )
    {
        options = null;

        if (listen is null)
        {
            error = "The listen section is required.";
            return false;
        }

        if (listen.UnixSocket is not { } socket)
        {
            error = "The listen section must configure unix_socket.";
            return false;
        }

        if (socket.Path is not { } path)
        {
            error = "The listen.unix_socket.path value is required.";
            return false;
        }

        if (!TryValidateSocketPath(path, out error)
            || !TryValidateSocketMode(socket.Mode, out var mode, out error))
        {
            return false;
        }

        options = new ListenOptions(new UnixSocketOptions(path, mode));
        error = null;
        return true;
    }

    private static bool TryValidateSocketMode(
        string? configured,
        out UnixFileMode mode,
        [NotNullWhen(false)] out string? error
    )
    {
        mode = DefaultSocketMode;

        if (configured is null)
        {
            error = null;
            return true;
        }

        // Octal, as an operator already writes a mode. A leading zero is accepted;
        // setuid, setgid, and sticky are not.
        if (configured.Length is < 3 or > 4
            || !configured.All(static character => character is >= '0' and <= '7')
            || (configured.Length == 4 && configured[0] != '0'))
        {
            error = "The listen.unix_socket.mode value must be three octal digits, such as \"0660\".";
            return false;
        }

        var parsed = (UnixFileMode) Convert.ToInt32(configured, 8);

        // Anyone who can write the socket can mint for the whole allowlist, so the
        // one mode that cannot be a deliberate choice is refused.
        if (parsed.HasFlag(UnixFileMode.OtherWrite))
        {
            error = "The listen.unix_socket.mode value must not grant write to others.";
            return false;
        }

        if (!parsed.HasFlag(UnixFileMode.UserRead) || !parsed.HasFlag(UnixFileMode.UserWrite))
        {
            error = "The listen.unix_socket.mode value must grant read and write to the owner.";
            return false;
        }

        mode = parsed;
        error = null;
        return true;
    }

    private static bool TryValidateSocketPath(
        string unixSocketPath,
        [NotNullWhen(false)] out string? error
    )
    {
        if (unixSocketPath.Length == 0)
        {
            error = "The listen.unix_socket.path value must not be empty.";
            return false;
        }

        if (!Path.IsPathRooted(unixSocketPath))
        {
            error = "The listen.unix_socket.path value must be absolute.";
            return false;
        }

        // The kernel caps sockaddr_un.sun_path, and exceeding it otherwise surfaces
        // as an unhandled error from deep inside the server rather than a
        // configuration problem an operator can act on.
        var maximumSocketPathLength = OperatingSystem.IsMacOS() ? 104 : 108;
        if (Encoding.UTF8.GetByteCount(unixSocketPath) > maximumSocketPathLength)
        {
            error =
                $"The listen.unix_socket.path value must be at most {maximumSocketPathLength} bytes on this platform.";

            return false;
        }

        error = null;
        return true;
    }
}
