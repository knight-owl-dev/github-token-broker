using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration.Validation;


namespace KnightOwl.GitHubTokenBroker.Core.Tests.Validation;

public sealed class RefreshMarginValidatorTests
{
    [Fact]
    public void DefaultsWhenUnconfigured()
    {
        Assert.True(RefreshMarginValidator.TryValidate(null, out var margin, out _));
        Assert.Equal(BrokerConfiguration.DefaultRefreshMargin, margin);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(1800)]
    public void AcceptsTheInclusiveBounds(int seconds)
    {
        Assert.True(RefreshMarginValidator.TryValidate(seconds, out var margin, out _));
        Assert.Equal(TimeSpan.FromSeconds(seconds), margin);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(1801)]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesOutsideThem(int seconds)
    {
        Assert.False(RefreshMarginValidator.TryValidate(seconds, out _, out var error));
        Assert.Equal("The token_refresh_margin_seconds value must be between 30 and 1800.", error);
    }
}
