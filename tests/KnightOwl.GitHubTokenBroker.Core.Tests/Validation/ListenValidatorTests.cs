using System.Net;
using System.Text;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Json;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Core.Tests.Validation;

public sealed class ListenValidatorTests
{
    // Mirrors the kernel's sockaddr_un.sun_path cap. Duplicated so the tests below
    // pin where the boundary falls rather than that some long path is refused.
    private static readonly int MaximumSocketPathLength = OperatingSystem.IsMacOS() ? 104 : 108;

    [Fact]
    public void AcceptsASocketAlone()
    {
        Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock"
                },
                out var options,
                out _
            )
        );

        Assert.Equal("/run/broker.sock", options.UnixSocket!.Path);
        Assert.Equal(ListenValidator.DefaultSocketMode, options.UnixSocket.Mode);
        Assert.Null(options.Tcp);
    }

    [Fact]
    public void DefaultsTheSocketModeToOwnerOnly()
        => Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            ListenValidator.DefaultSocketMode
        );

    [Theory]
    [InlineData("0600", UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData("600", UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData(
        "0660",
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
    )]
    [InlineData(
        "0664",
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite
        | UnixFileMode.OtherRead
    )]
    public void AcceptsASocketMode(string configured, UnixFileMode expected)
    {
        Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock",
                    UnixSocketMode = configured
                },
                out var options,
                out _
            )
        );

        Assert.Equal(expected, options.UnixSocket!.Mode);
    }

    /// <param name="configured">The unix_socket_mode as an operator wrote it.</param>
    /// <param name="expected">The complete rejection message.</param>
    /// <remarks>
    /// Anyone who can write the socket can mint for the whole allowlist, which
    /// is what makes a mode granting write to others unusable as a choice.
    /// </remarks>
    [Theory]
    [InlineData("0666", "The listen.unix_socket_mode value must not grant write to others.")]
    [InlineData("0777", "The listen.unix_socket_mode value must not grant write to others.")]
    [InlineData("0400", "The listen.unix_socket_mode value must grant read and write to the owner.")]
    public void RefusesAnUnsafeSocketMode(string configured, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock",
                    UnixSocketMode = configured
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    /// <param name="configured">The unix_socket_mode as an operator wrote it.</param>
    /// <remarks>
    /// A decimal that looks octal, a non-digit, both wrong lengths, and setuid.
    /// </remarks>
    [Theory]
    [InlineData("0680")]
    [InlineData("06o0")]
    [InlineData("60")]
    [InlineData("06600")]
    [InlineData("4600")]
    public void RefusesAMalformedSocketMode(string configured)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock",
                    UnixSocketMode = configured
                },
                out _,
                out var error
            )
        );

        Assert.Equal(
            "The listen.unix_socket_mode value must be three octal digits, such as \"0660\".",
            error
        );
    }

    [Fact]
    public void RefusesASocketModeWithNoSocket()
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(),
                    UnixSocketMode = "0660"
                },
                out _,
                out var error
            )
        );

        Assert.Equal("The listen.unix_socket_mode setting requires listen.unix_socket.", error);
    }

    [Fact]
    public void AcceptsTcpAlone()
    {
        Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp()
                },
                out var options,
                out _
            )
        );

        Assert.Null(options.UnixSocket);
        Assert.Equal(IPAddress.Loopback, options.Tcp!.Address);
        Assert.Equal(8765, options.Tcp.Port);
        Assert.Equal("/run/broker.credential", options.Tcp.ClientCredentialPath);
    }

    [Fact]
    public void AcceptsBothAtOnce()
    {
        Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock",
                    Tcp = Tcp()
                },
                out var options,
                out _
            )
        );

        Assert.NotNull(options.UnixSocket);
        Assert.NotNull(options.Tcp);
    }

    [Fact]
    public void RefusesAnAbsentListenSection()
    {
        Assert.False(ListenValidator.TryValidate(null, out _, out var error));
        Assert.Equal("The listen section is required.", error);
    }

    [Fact]
    public void RefusesAnEmptyListenSection()
    {
        Assert.False(ListenValidator.TryValidate(new ListenDocument(), out _, out var error));
        Assert.Equal("The listen section must configure unix_socket, tcp, or both.", error);
    }

    [Theory]
    [InlineData("", "The listen.unix_socket path must not be empty.")]
    [InlineData("broker.sock", "The listen.unix_socket path must be absolute.")]
    [InlineData("run/broker.sock", "The listen.unix_socket path must be absolute.")]
    public void RefusesABadSocketPath(string path, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = path
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    [Fact]
    public void AcceptsASocketPathExactlyAtTheLimit()
        => Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = SocketPathOfBytes(MaximumSocketPathLength)
                },
                out _,
                out _
            )
        );

    [Fact]
    public void RefusesASocketPathOneByteOver()
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = SocketPathOfBytes(MaximumSocketPathLength + 1)
                },
                out _,
                out var error
            )
        );

        Assert.Equal(
            $"The listen.unix_socket path must be at most {MaximumSocketPathLength} bytes on this platform.",
            error
        );
    }

    [Fact]
    public void MeasuresTheSocketPathInBytesRatherThanCharacters()
    {
        // Each of these is three UTF-8 bytes, so a path well inside the character
        // count is still over the byte cap.
        var multiByte = "/" + new string('é', MaximumSocketPathLength - 1);
        Assert.True(multiByte.Length <= MaximumSocketPathLength);

        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = multiByte
                },
                out _,
                out var error
            )
        );

        Assert.Contains("bytes", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void AcceptsThePortBounds(int port)
        => Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(port: port)
                },
                out _,
                out _
            )
        );

    [Theory]
    [InlineData(0, "The listen.tcp.port value must be between 1 and 65535.")]
    [InlineData(-1, "The listen.tcp.port value must be between 1 and 65535.")]
    [InlineData(65536, "The listen.tcp.port value must be between 1 and 65535.")]
    public void RefusesAPortOutsideThem(int port, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(port: port)
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    [Fact]
    public void RefusesAnAbsentPort()
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(port: null)
                },
                out _,
                out var error
            )
        );

        Assert.Equal("The listen.tcp.port value is required.", error);
    }

    [Theory]
    [InlineData(null, "The listen.tcp.address value is required.")]
    [InlineData("not-an-ip", "The listen.tcp.address value must be an IP address.")]
    [InlineData("127.0.0.1:8765", "The listen.tcp.address value must be an IP address.")]
    public void RefusesABadAddress(string? address, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(address: address)
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    /// <param name="address">Loopback, private, link-local, or unique-local.</param>
    /// <remarks>
    /// <c>172.17.0.1</c> is the Docker bridge, which is how a container reaches
    /// its host — the deployment this transport exists for.
    /// </remarks>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("172.17.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.1.1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public void AcceptsAnAddressOnlyThisHostOrItsNetworkCanReach(string address)
    {
        Assert.True(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(address: address)
                },
                out _,
                out _
            )
        );
    }

    /// <param name="address">A public address, or a wildcard.</param>
    /// <remarks>
    /// A wildcard is refused with the public addresses because it covers every
    /// interface the host has, including ones it may grow later.
    /// </remarks>
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700::1111")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void RefusesAnAddressBeyondThisHostsNetwork(string address)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(address: address)
                },
                out _,
                out var error
            )
        );

        Assert.Contains("loopback or private", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "The listen.tcp.client_credential_path value is required for TCP.")]
    [InlineData("relative", "The listen.tcp.client_credential_path value must be absolute.")]
    public void RefusesABadCredentialPath(string? path, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    Tcp = Tcp(credentialPath: path)
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    [Fact]
    public void RefusesTcpEvenWhenAValidSocketIsAlsoConfigured()
    {
        // A usable socket must not mask a misconfigured second transport.
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = "/run/broker.sock",
                    Tcp = Tcp(credentialPath: null),
                },
                out _,
                out var error
            )
        );

        Assert.StartsWith("The listen.tcp.", error, StringComparison.Ordinal);
    }

    private static TcpDocument Tcp(
        string? address = "127.0.0.1",
        int? port = 8765,
        string? credentialPath = "/run/broker.credential"
    )
        => new()
        {
            Address = address,
            Port = port,
            ClientCredentialPath = credentialPath,
        };

    private static string SocketPathOfBytes(int bytes)
    {
        var path = "/" + new string('a', bytes - 1);
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(path));
        return path;
    }
}
