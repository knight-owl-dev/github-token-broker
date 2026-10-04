using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

[UnsupportedOSPlatform("windows")]
public sealed class UnixFileStatusTests : IDisposable
{
    // Kept short: a socket path is limited to about 104 bytes.
    private readonly string _root = Directory.CreateTempSubdirectory("hgt").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Path(string name)
        => System.IO.Path.Combine(_root, name);

    private static Socket Bind(string socketPath)
    {
        Socket bound = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(socketPath));
        return bound;
    }

    /// <returns>This process's effective user id, as <c>id -u</c> reports it.</returns>
    private static uint EffectiveUserId()
    {
        using var id = Process.Start(new ProcessStartInfo("id", "-u") { RedirectStandardOutput = true })!;
        var output = id.StandardOutput.ReadToEnd();
        id.WaitForExit();
        return uint.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void ReadsASocket()
    {
        var socketPath = Path("bound.sock");
        using var bound = Bind(socketPath);

        Assert.Equal(UnixFileKind.Socket, UnixFileStatus.Of(socketPath).Kind);
    }

    [Fact]
    public void ReadsALinkRatherThanItsTarget()
    {
        var socketPath = Path("bound.sock");
        var link = Path("link");
        using var bound = Bind(socketPath);
        File.CreateSymbolicLink(link, socketPath);

        Assert.Equal(UnixFileKind.SymbolicLink, UnixFileStatus.Of(link).Kind);
    }

    [Fact]
    public void FollowsALinkToItsTarget()
    {
        var socketPath = Path("bound.sock");
        var link = Path("link");
        using var bound = Bind(socketPath);
        File.CreateSymbolicLink(link, socketPath);

        Assert.Equal(UnixFileKind.Socket, UnixFileStatus.OfTarget(link).Kind);
    }

    [Fact]
    public void ReadsARegularFile()
    {
        var filePath = Path("file");
        File.WriteAllText(filePath, string.Empty);

        Assert.Equal(UnixFileKind.Other, UnixFileStatus.Of(filePath).Kind);
    }

    [Fact]
    public void ReadsADirectory()
        => Assert.Equal(UnixFileKind.Other, UnixFileStatus.Of(_root).Kind);

    [Fact]
    public void ReadsAMissingPath()
        => Assert.Equal(new UnixFileStatus(UnixFileKind.Missing, null), UnixFileStatus.Of(Path("absent")));

    [Fact]
    public void ReadsTheOwnerOfAFileThisProcessCreated()
    {
        var filePath = Path("owned");
        File.WriteAllText(filePath, string.Empty);

        Assert.Equal(EffectiveUserId(), UnixFileStatus.Of(filePath).OwnerId);
    }

    /// <remarks>
    /// Paired with the case above, which reads a non-zero id where this platform
    /// runs unprivileged, so an offset landing on zeroed padding fails one of them.
    /// </remarks>
    [Fact]
    public void ReadsRootAsTheOwnerOfTheFilesystemRoot()
        => Assert.Equal(0u, UnixFileStatus.OfTarget("/").OwnerId);
}
