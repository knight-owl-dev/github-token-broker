using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Core.Tests.Validation;

public sealed class ApiUrlValidatorTests
{
    [Fact]
    public void DefaultsWhenUnconfigured()
    {
        Assert.True(ApiUrlValidator.TryValidate(null, out var uri, out _));
        Assert.Equal(new Uri(BrokerConfiguration.DefaultApiUrl), uri);
    }

    /// <param name="configured">The api_url as an operator wrote it.</param>
    /// <remarks>
    /// Plaintext is accepted against loopback because that is the seam a local
    /// fake GitHub uses in a test.
    /// </remarks>
    [Theory]
    [InlineData("https://api.github.com")]
    [InlineData("https://github.example.com/api/v3")]
    [InlineData("http://127.0.0.1:8123")]
    [InlineData("http://localhost:8123")]
    [InlineData("http://[::1]:8123")]
    public void Accepts(string configured)
        => Assert.True(ApiUrlValidator.TryValidate(configured, out _, out _));

    /// <param name="configured">The api_url as an operator wrote it.</param>
    /// <param name="expected">The complete rejection message.</param>
    /// <remarks>
    /// A bare path parses as an absolute <c>file:</c> URI, so the scheme rule is
    /// what catches it rather than the absoluteness rule.
    /// </remarks>
    [Theory]
    [InlineData("api.github.com", "The api_url value must be an absolute URL.")]
    [InlineData("https://api.github.com?x=1", "The api_url value must not carry a query or fragment.")]
    [InlineData("https://api.github.com#frag", "The api_url value must not carry a query or fragment.")]
    [InlineData("http://api.github.com", "The api_url value must use https, or http only against loopback.")]
    [InlineData("ftp://api.github.com", "The api_url value must use https, or http only against loopback.")]
    [InlineData("/relative", "The api_url value must use https, or http only against loopback.")]
    public void Refuses(string configured, string expected)
    {
        Assert.False(ApiUrlValidator.TryValidate(configured, out _, out var error));
        Assert.Equal(expected, error);
    }
}
