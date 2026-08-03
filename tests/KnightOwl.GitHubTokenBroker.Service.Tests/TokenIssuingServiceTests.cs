using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

/// <summary>
/// What the service adds over the cache it holds: which failures cost every
/// stored grant, and whose cancellation the mint answers to.
/// </summary>
/// <remarks>
/// Driven through the real <see cref="InstallationTokenCache"/>, so the assertion is
/// that the grants are gone. Two repositories are needed for that: with one, a
/// dropped grant and a grant that had gone stale anyway both re-mint.
/// </remarks>
public sealed class TokenIssuingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private static readonly RepositoryAccessPolicy Held = TestPolicies.Policy("owner/held");
    private static readonly RepositoryAccessPolicy Failing = TestPolicies.Policy("owner/failing");

    private readonly ScriptedTokenIssuer _issuer = new();
    private readonly TokenIssuingService _service;

    public TokenIssuingServiceTests()
        => _service = new TokenIssuingService(
            new InstallationTokenCache(new TestTimeProvider(Now), Margin),
            _issuer
        );

    /// <remarks>The property that makes a compromised key safe to rotate.</remarks>
    [Fact]
    public async Task DropsEveryStoredGrantWhenTheAppCredentialIsRejected()
    {
        var held = await MintAndHold();

        await FailWith(TokenIssuanceFailure.AppUnauthorized);

        // The held grant has 59 minutes left, so only the drop can explain a mint.
        var replacement = await _service.IssueAsync(Held, TestContext.Current.CancellationToken);

        Assert.Equal(3, _issuer.Mints);
        Assert.NotSame(held, replacement);
    }

    /// <param name="failure">A failure that leaves the App credential standing.</param>
    /// <remarks>
    /// The paired half: the failure value is the only difference, so the test above
    /// cannot pass for a service that clears on everything.
    /// </remarks>
    [Theory]
    [InlineData(TokenIssuanceFailure.InstallationForbidden)]
    [InlineData(TokenIssuanceFailure.InstallationOrRepositoryMissing)]
    [InlineData(TokenIssuanceFailure.PermissionDrift)]
    [InlineData(TokenIssuanceFailure.UntrustworthyResponse)]
    [InlineData(TokenIssuanceFailure.Unavailable)]
    [InlineData(TokenIssuanceFailure.PrivateKeyUnusable)]
    public async Task KeepsStoredGrantsWhenTheAppCredentialStands(TokenIssuanceFailure failure)
    {
        var held = await MintAndHold();

        await FailWith(failure);

        var served = await _service.IssueAsync(Held, TestContext.Current.CancellationToken);

        Assert.Equal(2, _issuer.Mints);
        Assert.Same(held, served);
    }

    /// <remarks>
    /// The guarantee is a token the mint cannot be cancelled through, so
    /// <see cref="CancellationToken.CanBeCanceled"/> is what tells one from the other.
    /// </remarks>
    [Fact]
    public async Task MintsOutsideTheCallersCancellation()
    {
        using CancellationTokenSource abandoning = new();

        await _service.IssueAsync(Held, abandoning.Token);

        Assert.True(abandoning.Token.CanBeCanceled);
        Assert.False(_issuer.LastCancellationToken.CanBeCanceled);
    }

    /// <summary>Mints a grant for <see cref="Held"/> and proves it was cached.</summary>
    /// <returns>The grant now held, which nothing but a drop can dislodge.</returns>
    private async Task<Domain.Tokens.InstallationToken> MintAndHold()
    {
        var minted = await _service.IssueAsync(Held, TestContext.Current.CancellationToken);
        var reused = await _service.IssueAsync(Held, TestContext.Current.CancellationToken);

        Assert.Equal(1, _issuer.Mints);
        Assert.Same(minted, reused);

        return minted;
    }

    /// <summary>Fails one mint for <see cref="Failing"/>, leaving the issuer minting.</summary>
    /// <param name="failure">The classification GitHub's refusal earns.</param>
    private async Task FailWith(TokenIssuanceFailure failure)
    {
        _issuer.Failure = failure;

        var thrown = await Assert.ThrowsAsync<TokenIssuanceException>(()
            => _service.IssueAsync(Failing, TestContext.Current.CancellationToken)
        );

        // The classification survives the rethrow, which is what the API maps.
        Assert.Equal(failure, thrown.Failure);
        Assert.Equal(2, _issuer.Mints);

        _issuer.Failure = null;
    }
}
