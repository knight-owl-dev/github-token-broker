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
                    UnixSocket = new UnixSocketDocument { Path = "/run/broker.sock" }
                },
                out var options,
                out _
            )
        );

        Assert.Equal("/run/broker.sock", options.UnixSocket.Path);
        Assert.Equal(ListenValidator.DefaultSocketMode, options.UnixSocket.Mode);
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
                    UnixSocket = new UnixSocketDocument { Path = "/run/broker.sock", Mode = configured }
                },
                out var options,
                out _
            )
        );

        Assert.Equal(expected, options.UnixSocket.Mode);
    }

    /// <param name="configured">The unix_socket.mode as an operator wrote it.</param>
    /// <param name="expected">The complete rejection message.</param>
    /// <remarks>
    /// Anyone who can write the socket can mint for the whole allowlist, which
    /// is what makes a mode granting write to others unusable as a choice.
    /// </remarks>
    [Theory]
    [InlineData("0666", "The listen.unix_socket.mode value must not grant write to others.")]
    [InlineData("0777", "The listen.unix_socket.mode value must not grant write to others.")]
    [InlineData("0400", "The listen.unix_socket.mode value must grant read and write to the owner.")]
    public void RefusesAnUnsafeSocketMode(string configured, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = new UnixSocketDocument { Path = "/run/broker.sock", Mode = configured }
                },
                out _,
                out var error
            )
        );

        Assert.Equal(expected, error);
    }

    /// <param name="configured">The unix_socket.mode as an operator wrote it.</param>
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
                    UnixSocket = new UnixSocketDocument { Path = "/run/broker.sock", Mode = configured }
                },
                out _,
                out var error
            )
        );

        Assert.Equal(
            "The listen.unix_socket.mode value must be three octal digits, such as \"0660\".",
            error
        );
    }

    /// <remarks>
    /// Named apart from the empty section: a socket with only a mode is an
    /// operator halfway through configuring, not one who has not started.
    /// </remarks>
    [Fact]
    public void RefusesASocketWithNoPath()
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = new UnixSocketDocument { Mode = "0660" }
                },
                out _,
                out var error
            )
        );

        Assert.Equal("The listen.unix_socket.path value is required.", error);
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
        Assert.Equal("The listen section must configure unix_socket.", error);
    }

    [Theory]
    [InlineData("", "The listen.unix_socket.path value must not be empty.")]
    [InlineData("broker.sock", "The listen.unix_socket.path value must be absolute.")]
    [InlineData("run/broker.sock", "The listen.unix_socket.path value must be absolute.")]
    public void RefusesABadSocketPath(string path, string expected)
    {
        Assert.False(
            ListenValidator.TryValidate(
                new ListenDocument
                {
                    UnixSocket = new UnixSocketDocument { Path = path }
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
                    UnixSocket = new UnixSocketDocument { Path = SocketPathOfBytes(MaximumSocketPathLength) }
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
                    UnixSocket = new UnixSocketDocument { Path = SocketPathOfBytes(MaximumSocketPathLength + 1) }
                },
                out _,
                out var error
            )
        );

        Assert.Equal(
            $"The listen.unix_socket.path value must be at most {MaximumSocketPathLength} bytes on this platform.",
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
                    UnixSocket = new UnixSocketDocument { Path = multiByte }
                },
                out _,
                out var error
            )
        );

        Assert.Contains("bytes", error, StringComparison.Ordinal);
    }

    private static string SocketPathOfBytes(int bytes)
    {
        var path = "/" + new string('a', bytes - 1);
        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(path));
        return path;
    }
}
