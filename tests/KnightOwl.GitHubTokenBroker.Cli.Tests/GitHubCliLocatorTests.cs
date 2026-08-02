using System.Runtime.Versioning;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;
using KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

// The executable bit is what the locator selects on, and it has no Windows
// meaning. The attribute says so where a runtime guard would leave the
// assertions silently skipped.
[UnsupportedOSPlatform("windows")]
public sealed class GitHubCliLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hga-locate").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string CreateExecutable(string directory, string name)
        => TestExecutable.Create(Path.Combine(_root, directory, name));

    private string CreateWithoutExecuteBit(string directory, string name)
        => TestExecutable.CreateWithoutExecuteBit(Path.Combine(_root, directory, name));

    /// <remarks>
    /// Paired so the skip is the missing execute bit rather than the candidate
    /// never being reachable: the same directory holds both, and the executable
    /// one is found.
    /// </remarks>
    [Fact]
    public void SkipsACandidateItCouldNotExecute()
    {
        CreateWithoutExecuteBit("bin", "gh");
        var usable = CreateExecutable("later", "gh");

        Assert.True(
            GitHubCliLocator.TryLocate(
                configuredPath: null,
                $"{Path.Combine(_root, "bin")}{Path.PathSeparator}{Path.Combine(_root, "later")}",
                ownExecutablePath: null,
                out var located,
                out _
            )
        );

        Assert.Equal(usable, located);
    }

    [Fact]
    public void RefusesAConfiguredPathItCouldNotExecute()
    {
        var configured = CreateWithoutExecuteBit("explicit", "gh");

        Assert.False(
            GitHubCliLocator.TryLocate(
                configured,
                searchPath: null,
                ownExecutablePath: null,
                out _,
                out var error
            )
        );

        Assert.Contains("not executable", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PrefersAnExplicitlyConfiguredPath()
    {
        var configured = CreateExecutable("explicit", "gh");
        var onPath = CreateExecutable("bin", "gh");

        Assert.True(
            GitHubCliLocator.TryLocate(
                configured,
                Path.GetDirectoryName(onPath),
                null,
                out var located,
                out _
            )
        );

        Assert.Equal(configured, located);
    }

    [Fact]
    public void RejectsAConfiguredPathThatDoesNotExist()
    {
        Assert.False(
            GitHubCliLocator.TryLocate(
                Path.Combine(_root, "absent"),
                null,
                null,
                out _,
                out var error
            )
        );

        Assert.Contains("does not exist", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindsTheFirstMatchOnThePath()
    {
        var first = CreateExecutable("one", "gh");
        var second = CreateExecutable("two", "gh");
        var searchPath = string.Join(
            Path.PathSeparator,
            Path.GetDirectoryName(first),
            Path.GetDirectoryName(second)
        );

        Assert.True(GitHubCliLocator.TryLocate(null, searchPath, null, out var located, out _));

        Assert.Equal(first, located);
    }

    [Fact]
    public void SkipsItselfSoTheWrapperCannotLaunchItself()
    {
        // Deployments put the wrapper on PATH under its own name, but an operator can
        // also install it as "gh". Selecting it would hand the gh arguments back to
        // this executable, which answers with its own usage.
        var wrapper = CreateExecutable("wrapper", "gh");
        var real = CreateExecutable("real", "gh");
        var searchPath = string.Join(
            Path.PathSeparator,
            Path.GetDirectoryName(wrapper),
            Path.GetDirectoryName(real)
        );

        Assert.True(GitHubCliLocator.TryLocate(null, searchPath, wrapper, out var located, out _));

        Assert.Equal(real, located);
    }

    [Fact]
    public void SkipsItselfThroughASymbolicLink()
    {
        // Installing the wrapper as a link named "gh" is the documented deployment.
        // Environment.ProcessPath is already resolved on Linux, so comparing paths
        // without following links misses this and the wrapper selects itself.
        var wrapper = CreateExecutable("wrapper", "github-token");
        var linkDirectory = Path.Combine(_root, "link");
        Directory.CreateDirectory(linkDirectory);
        var link = Path.Combine(linkDirectory, "gh");
        File.CreateSymbolicLink(link, wrapper);
        var real = CreateExecutable("real", "gh");

        Assert.True(
            GitHubCliLocator.TryLocate(
                null,
                string.Join(Path.PathSeparator, linkDirectory, Path.GetDirectoryName(real)),
                wrapper,
                out var located,
                out _
            )
        );

        Assert.Equal(real, located);
    }

    [Fact]
    public void FailsWhenOnlyItselfIsOnThePath()
    {
        var wrapper = CreateExecutable("only", "gh");

        Assert.False(
            GitHubCliLocator.TryLocate(
                null,
                Path.GetDirectoryName(wrapper),
                wrapper,
                out _,
                out var error
            )
        );

        Assert.Contains("No GitHub CLI", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FailsOnAnEmptySearchPath(string? searchPath)
        => Assert.False(GitHubCliLocator.TryLocate(null, searchPath, null, out _, out _));

    [Fact]
    public void SkipsUnusableSearchPathEntries()
    {
        var real = CreateExecutable("real", "gh");
        var searchPath = string.Join(
            Path.PathSeparator,
            "\0invalid",
            Path.Combine(_root, "does-not-exist"),
            Path.GetDirectoryName(real)
        );

        Assert.True(GitHubCliLocator.TryLocate(null, searchPath, null, out var located, out _));

        Assert.Equal(real, located);
    }
}
