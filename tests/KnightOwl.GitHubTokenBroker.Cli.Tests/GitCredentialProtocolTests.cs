using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Git;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests;

public sealed class GitCredentialProtocolTests
{
    [Fact]
    public void ReadsTheFieldsThisClientActsOn()
    {
        var request = GitCredentialProtocol.Read(new StringReader("protocol=https\nhost=github.com\npath=owner/repo.git\n\n"));

        Assert.Equal("https", request.Protocol);
        Assert.Equal("github.com", request.Host);
        Assert.Equal("owner/repo.git", request.Path);
    }

    [Fact]
    public void StopsAtTheBlankLineSoAPasswordAfterItIsNeverRead()
    {
        var request = GitCredentialProtocol.Read(new StringReader("protocol=https\nhost=github.com\n\npassword=hunter2\n"));

        Assert.Equal("github.com", request.Host);
    }

    [Fact]
    public void ToleratesInputWithNoTrailingNewline()
    {
        var request = GitCredentialProtocol.Read(new StringReader("host=github.com"));

        Assert.Equal("github.com", request.Host);
    }

    [Fact]
    public void KeepsAnEqualsSignInsideAValue()
    {
        var request = GitCredentialProtocol.Read(new StringReader("path=owner/repo=x\n"));

        Assert.Equal("owner/repo=x", request.Path);
    }

    [Fact]
    public void RefusesOneEnormousLineWithoutMaterializingIt()
    {
        // A single unterminated line is the case a length check on ReadLine's result
        // cannot catch, because the allocation has already happened by then.
        Assert.Throws<InvalidDataException>(() => GitCredentialProtocol.Read(new StringReader("host=" + new string('a', 100_000))));
    }

    [Fact]
    public void RefusesTooManyLines()
        => Assert.Throws<InvalidDataException>(() => GitCredentialProtocol.Read(new StringReader(string.Concat(Enumerable.Repeat("k=v\n", 200)))));

    [Fact]
    public void RefusesInputThatExceedsTheBudgetAcrossLines()
        => Assert.Throws<InvalidDataException>(()
            => GitCredentialProtocol.Read(new StringReader(string.Concat(Enumerable.Repeat("k=" + new string('a', 500) + "\n", 40))))
        );

    [Fact]
    public void WritesTheCredentialGitExpects()
    {
        StringWriter output = new();

        GitCredentialProtocol.WriteCredential(output, "ghs_opaque");

        Assert.Equal(
            $"username={GitCredentialProtocol.TokenUsername}\npassword=ghs_opaque\n\n",
            output.ToString()
        );
    }

    /// <param name="token">A token carrying whitespace or a control character.</param>
    /// <remarks>
    /// The format is line-based, so a newline inside a token would append fields
    /// of the writer's choosing to the credential Git reads.
    /// </remarks>
    [Theory]
    [InlineData("ghs_x\nusername=attacker")]
    [InlineData("ghs_x\r\npassword=other")]
    [InlineData("ghs_x\0y")]
    [InlineData("ghs x")]
    public void RefusesATokenThatWouldEndTheValueEarly(string token)
    {
        StringWriter output = new();

        Assert.Throws<InvalidDataException>(() => GitCredentialProtocol.WriteCredential(output, token));
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void KeepsTheTokenOpaqueOtherwise()
    {
        StringWriter output = new();

        // No prefix, length, or alphabet is assumed, so punctuation passes.
        GitCredentialProtocol.WriteCredential(output, "!@#$%^&*()_+-=[]{}|;:',.<>/?~`");

        Assert.Contains("password=!@#", output.ToString(), StringComparison.Ordinal);
    }
}
