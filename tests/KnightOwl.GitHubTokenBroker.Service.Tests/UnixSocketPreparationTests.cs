using System.Net.Sockets;
using System.Runtime.Versioning;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

// Mode assertions are the point of half of these, and the broker publishes only
// Linux and macOS runtimes.
[UnsupportedOSPlatform("windows")]
public sealed class UnixSocketPreparationTests : IDisposable
{
    // Kept short: a socket path is limited to about 104 bytes.
    private readonly string _root = Directory.CreateTempSubdirectory("hga").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Path(string name)
        => System.IO.Path.Combine(_root, name);

    [Fact]
    public void AcceptsAPathThatDoesNotExist()
        => UnixSocketPreparation.Prepare(Path("absent.sock"));

    [Fact]
    public void RefusesASocketWithNoListenerRatherThanRemovingIt()
    {
        var socketPath = Path("orphan.sock");

        // Bound but never listening, so the kernel refuses connections to it. That is
        // the same answer a socket orphaned by an unclean shutdown gives — and the
        // same answer Linux gives for an ordinary file, which is why neither is removed.
        using Socket bound = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        bound.Bind(new UnixDomainSocketEndPoint(socketPath));

        var failure = Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(socketPath));

        Assert.Contains("is occupied", failure.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(socketPath));
    }

    [Fact]
    public void RefusesToTouchALiveSocket()
    {
        var socketPath = Path("live.sock");
        using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        var failure = Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(socketPath));

        Assert.Contains("already listening", failure.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(socketPath));
    }

    [Fact]
    public void RefusesADirectory()
    {
        var directoryPath = Path("a-directory");
        Directory.CreateDirectory(directoryPath);

        Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(directoryPath));
        Assert.True(Directory.Exists(directoryPath));
    }

    [Fact]
    public void RefusesASymbolicLinkWithoutFollowingIt()
    {
        var target = Path("target");
        var link = Path("link.sock");
        File.WriteAllText(target, "important");
        File.CreateSymbolicLink(link, target);

        Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(link));

        // Neither the link nor what it points at may be removed.
        Assert.True(File.Exists(link));
        Assert.Equal("important", File.ReadAllText(target));
    }

    /// <remarks>
    /// A link to nothing reads as a free path under POSIX, where stat follows it.
    /// .NET falls back to lstat and reports the entry, so the check above catches
    /// this too. Pinned because that is a framework behavior rather than one this
    /// code arranges. The message is asserted, since refusal as an occupied path
    /// would pass without naming the link.
    /// </remarks>
    [Fact]
    public void RefusesASymbolicLinkPointingAtNothing()
    {
        var link = Path("dangling.sock");
        File.CreateSymbolicLink(link, Path("absent"));

        var failure = Assert.Throws<ConfigurationException>(
            () => UnixSocketPreparation.Prepare(link)
        );

        Assert.Contains("symbolic link", failure.Message, StringComparison.Ordinal);
    }

    /// <param name="contents">What the file at the socket path holds.</param>
    /// <remarks>
    /// The empty case is the one a length heuristic would have misread as a
    /// socket, since a socket inode also reports zero bytes.
    /// </remarks>
    [Theory]
    [InlineData("not a socket")]
    [InlineData("")]
    public void RefusesARegularFile(string contents)
    {
        var filePath = Path("data.sock");
        File.WriteAllText(filePath, contents);

        var failure = Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(filePath));

        Assert.Contains("is occupied", failure.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(filePath));
        Assert.Equal(contents, File.ReadAllText(filePath));
    }

    [Fact]
    public void CreatesAMissingParentDirectoryOwnerOnly()
    {
        var directory = Path("no-such-directory");

        UnixSocketPreparation.Prepare(System.IO.Path.Combine(directory, "broker.sock"));

        Assert.True(Directory.Exists(directory));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            new DirectoryInfo(directory).UnixFileMode
        );
    }

    [Fact]
    public void RefusesADirectoryOthersCanWrite()
    {
        var directory = Path("wide-open");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute
        );

        var failure = Assert.Throws<ConfigurationException>(() => UnixSocketPreparation.Prepare(System.IO.Path.Combine(directory, "broker.sock")));

        Assert.Contains("writable beyond its owner", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsAWideDirectoryThatIsSticky()
    {
        // What makes /tmp usable: everyone may create, only the owner may unlink, so
        // the socket cannot be replaced underneath the broker.
        var directory = Path("sticky");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute
            | UnixFileMode.StickyBit
        );

        UnixSocketPreparation.Prepare(System.IO.Path.Combine(directory, "broker.sock"));
    }

    [Fact]
    public void AcceptsADirectoryAGroupMayOnlyTraverse()
    {
        var directory = Path("group-traversal");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
        );

        UnixSocketPreparation.Prepare(System.IO.Path.Combine(directory, "broker.sock"));
    }

    [Fact]
    public void RejectsAnEmptyPath()
        => Assert.Throws<ArgumentException>(() => UnixSocketPreparation.Prepare(string.Empty));
}
