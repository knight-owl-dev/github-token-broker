using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class RepositoryNameTests
{
    [Theory]
    [InlineData("example-owner/example-repo")]
    [InlineData("a/b")]
    [InlineData("owner/.github")]
    [InlineData("owner/repo.with.dots")]
    [InlineData("owner/repo_with_underscores")]
    [InlineData("owner/repo-with-hyphens")]
    public void AcceptsCanonicalNames(string value)
        => Assert.True(RepositoryName.TryParse(value, out _, out _));

    /// <param name="value">A name a caller might supply.</param>
    /// <remarks>
    /// A URL where a name belongs, the wrong segment count, traversal, a query
    /// or fragment, whitespace anywhere including inside, hyphens at an edge,
    /// and empty parts. A <c>.git</c> suffix is refused because it belongs to a
    /// clone path rather than to a repository name.
    /// </remarks>
    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("/owner/repo")]
    [InlineData("owner/")]
    [InlineData("/")]
    [InlineData("owner/..")]
    [InlineData("owner/.")]
    [InlineData("../owner/repo")]
    [InlineData("owner/repo?x=1")]
    [InlineData("owner/repo#frag")]
    [InlineData(" owner/repo")]
    [InlineData("owner/repo ")]
    [InlineData("owner /repo")]
    [InlineData("owner/re po")]
    [InlineData("owner/repo.git")]
    [InlineData("-owner/repo")]
    [InlineData("owner-/repo")]
    [InlineData("")]
    public void RejectsMalformedNames(string value)
    {
        Assert.False(RepositoryName.TryParse(value, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\0")]
    [InlineData("\u007f")]
    public void RejectsControlCharacters(string control)
    {
        Assert.False(RepositoryName.TryParse($"owner/repo{control}", out _, out _));
        Assert.False(RepositoryName.TryParse($"own{control}er/repo", out _, out _));
        Assert.False(RepositoryName.TryParse($"{control}owner/repo", out _, out _));
    }

    [Fact]
    public void RejectsNull()
        => Assert.False(RepositoryName.TryParse(null, out _, out _));

    [Fact]
    public void RejectsOverlongParts()
    {
        Assert.False(RepositoryName.TryParse($"{new string('a', 40)}/repo", out _, out _));
        Assert.False(RepositoryName.TryParse($"owner/{new string('a', 101)}", out _, out _));
        Assert.True(RepositoryName.TryParse($"{new string('a', 39)}/repo", out _, out _));
        Assert.True(RepositoryName.TryParse($"owner/{new string('a', 100)}", out _, out _));
    }

    [Fact]
    public void KeyFoldsCaseWhileFullNamePreservesIt()
    {
        var mixed = RepositoryName.Parse("Example-Owner/Example-Repo");

        Assert.Equal("Example-Owner/Example-Repo", mixed.FullName);
        Assert.Equal("example-owner/example-repo", mixed.Key);
        Assert.Equal("Example-Owner", mixed.Owner);
        Assert.Equal("Example-Repo", mixed.Name);
    }

    [Fact]
    public void SpellingsThatDifferOnlyByCaseAreOneIdentity()
    {
        var lower = RepositoryName.Parse("owner/repo");
        var upper = RepositoryName.Parse("OWNER/REPO");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void ParseThrowsOnMalformedInput()
        => Assert.Throws<FormatException>(() => RepositoryName.Parse("owner/repo/extra"));
}
