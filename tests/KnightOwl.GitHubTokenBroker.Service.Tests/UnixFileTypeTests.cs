using System.Net.Sockets;
using System.Runtime.Versioning;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

[UnsupportedOSPlatform("windows")]
public sealed class UnixFileTypeTests : IDisposable
{
    // Kept short: a socket path is limited to about 104 bytes.
    private readonly string _root = Directory.CreateTempSubdirectory("hgt").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Path(string name)
        => System.IO.Path.Combine(_root, name);

    [Fact]
    public void ReadsASocket()
    {
        var socketPath = Path("bound.sock");
        using Socket bound = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(socketPath));

        Assert.Equal(UnixFileKind.Socket, UnixFileType.Of(socketPath));
    }

    [Fact]
    public void ReadsALinkRatherThanItsTarget()
    {
        var socketPath = Path("bound.sock");
        var link = Path("link");
        using Socket bound = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(socketPath));
        File.CreateSymbolicLink(link, socketPath);

        Assert.Equal(UnixFileKind.SymbolicLink, UnixFileType.Of(link));
    }

    [Fact]
    public void ReadsARegularFile()
    {
        var filePath = Path("file");
        File.WriteAllText(filePath, string.Empty);

        Assert.Equal(UnixFileKind.Other, UnixFileType.Of(filePath));
    }

    [Fact]
    public void ReadsADirectory()
        => Assert.Equal(UnixFileKind.Other, UnixFileType.Of(_root));

    [Fact]
    public void ReadsAMissingPath()
        => Assert.Equal(UnixFileKind.Missing, UnixFileType.Of(Path("absent")));
}
