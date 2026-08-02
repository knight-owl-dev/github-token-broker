using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;

/// <summary>Validates <c>listen</c> and its nested <c>listen.tcp</c>.</summary>
internal static class ListenValidator
{
    /// <summary>
    /// Mode given to the socket when the operator names none. Owner only, because
    /// widening it is what grants another account the right to mint.
    /// </summary>
    internal const UnixFileMode DefaultSocketMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Resolves the transports the broker will accept requests on.</summary>
    /// <param name="listen">The configured listeners, or <see langword="null"/>.</param>
    /// <param name="options">The listener options, when validation succeeds.</param>
    /// <param name="error">The complete rejection message, when it fails.</param>
    /// <returns><see langword="true"/> when at least one usable transport is configured.</returns>
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

        UnixSocketOptions? unixSocket = null;
        if (listen.UnixSocket is not null
            && !TryValidateUnixSocket(listen, out unixSocket, out error))
        {
            return false;
        }

        TcpListenerOptions? tcp = null;
        if (listen.Tcp is not null && !TryValidateTcp(listen.Tcp, out tcp, out error))
        {
            return false;
        }

        if (unixSocket is null && tcp is null)
        {
            error = "The listen section must configure unix_socket, tcp, or both.";
            return false;
        }

        if (unixSocket is null && listen.UnixSocketMode is not null)
        {
            error = "The listen.unix_socket_mode setting requires listen.unix_socket.";
            return false;
        }

        options = new ListenOptions(unixSocket, tcp);
        error = null;
        return true;
    }

    private static bool TryValidateUnixSocket(
        ListenDocument listen,
        [NotNullWhen(true)] out UnixSocketOptions? options,
        [NotNullWhen(false)] out string? error
    )
    {
        options = null;

        var path = listen.UnixSocket!;
        if (!TryValidateSocketPath(path, out error)
            || !TryValidateSocketMode(listen.UnixSocketMode, out var mode, out error))
        {
            return false;
        }

        options = new UnixSocketOptions(path, mode);
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
            error = "The listen.unix_socket_mode value must be three octal digits, such as \"0660\".";
            return false;
        }

        var parsed = (UnixFileMode) Convert.ToInt32(configured, 8);

        // Anyone who can write the socket can mint for the whole allowlist, so the
        // one mode that cannot be a deliberate choice is refused.
        if (parsed.HasFlag(UnixFileMode.OtherWrite))
        {
            error = "The listen.unix_socket_mode value must not grant write to others.";
            return false;
        }

        if (!parsed.HasFlag(UnixFileMode.UserRead) || !parsed.HasFlag(UnixFileMode.UserWrite))
        {
            error = "The listen.unix_socket_mode value must grant read and write to the owner.";
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
            error = "The listen.unix_socket path must not be empty.";
            return false;
        }

        if (!Path.IsPathRooted(unixSocketPath))
        {
            error = "The listen.unix_socket path must be absolute.";
            return false;
        }

        // The kernel caps sockaddr_un.sun_path, and exceeding it otherwise surfaces
        // as an unhandled error from deep inside the server rather than a
        // configuration problem an operator can act on.
        var maximumSocketPathLength = OperatingSystem.IsMacOS() ? 104 : 108;
        if (Encoding.UTF8.GetByteCount(unixSocketPath) > maximumSocketPathLength)
        {
            error =
                $"The listen.unix_socket path must be at most {maximumSocketPathLength} bytes on this platform.";

            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Whether only this host or its private network can reach the address.
    /// </summary>
    /// <param name="address">The configured listen address.</param>
    /// <returns><see langword="true"/> when the address is loopback or private.</returns>
    /// <remarks>
    /// A wildcard is refused along with public addresses: it covers every
    /// interface the host happens to have, including ones it may grow later, so
    /// the operator names the interface instead. Widening this needs TLS and a
    /// way to tell one caller from another, since the credential is shared.
    /// </remarks>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            172 => octets[1] is >= 16 and <= 31,
            192 => octets[1] == 168,
            169 => octets[1] == 254,
            _ => false,
        };
    }

    private static bool TryValidateTcp(
        TcpDocument tcp,
        [NotNullWhen(true)] out TcpListenerOptions? options,
        [NotNullWhen(false)] out string? error
    )
    {
        options = null;

        if (tcp.Address is not { } address)
        {
            error = "The listen.tcp.address value is required.";
            return false;
        }

        if (!IPAddress.TryParse(address, out var parsedAddress))
        {
            error = "The listen.tcp.address value must be an IP address.";
            return false;
        }

        if (!IsPrivate(parsedAddress))
        {
            error =
                "The listen.tcp.address value must be a loopback or private address. The client credential is sent in cleartext, so this transport reaches no further than the host and its private network.";

            return false;
        }

        if (tcp.Port is not { } port)
        {
            error = "The listen.tcp.port value is required.";
            return false;
        }

        if (port is < 1 or > 65535)
        {
            error = "The listen.tcp.port value must be between 1 and 65535.";
            return false;
        }

        if (tcp.ClientCredentialPath is not { } credentialPath)
        {
            error = "The listen.tcp.client_credential_path value is required for TCP.";
            return false;
        }

        if (!Path.IsPathRooted(credentialPath))
        {
            error = "The listen.tcp.client_credential_path value must be absolute.";
            return false;
        }

        options = new TcpListenerOptions(parsedAddress, port, credentialPath);
        error = null;
        return true;
    }
}
