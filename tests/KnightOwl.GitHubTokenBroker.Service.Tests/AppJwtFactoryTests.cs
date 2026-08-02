using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class AppJwtFactoryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SignsAVerifiableRs256Token()
    {
        using var key = TestKeys.CreateKey();
        var jwt = new AppJwtFactory(
            new StubPrivateKeySource(key),
            new TestTimeProvider(Now),
            123456
        ).Create();

        var segments = jwt.Value.Split('.');
        Assert.Equal(3, segments.Length);

        var signature = Base64Url.DecodeFromChars(segments[2]);
        var verified = key.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        Assert.True(verified);
    }

    [Fact]
    public void UsesTheRs256Header()
    {
        using var key = TestKeys.CreateKey();
        var jwt = new AppJwtFactory(
            new StubPrivateKeySource(key),
            new TestTimeProvider(Now),
            123456
        ).Create();

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Value.Split('.')[0]));

        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
    }

    [Fact]
    public void BackdatesIssuedAtAndStaysInsideTheTenMinuteCeiling()
    {
        using var key = TestKeys.CreateKey();
        var jwt = new AppJwtFactory(
            new StubPrivateKeySource(key),
            new TestTimeProvider(Now),
            123456
        ).Create();

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Value.Split('.')[1]));

        var issuedAt = payload.RootElement.GetProperty("iat").GetInt64();
        var expiresAt = payload.RootElement.GetProperty("exp").GetInt64();
        var nowSeconds = Now.ToUnixTimeSeconds();

        // Backdated, so GitHub never sees an issue time in its future.
        Assert.Equal(nowSeconds - 60, issuedAt);

        // GitHub refuses anything beyond ten minutes, measured from iat.
        Assert.True(expiresAt - issuedAt <= 600, $"lifetime was {expiresAt - issuedAt}s");
        Assert.True(expiresAt > nowSeconds);
    }

    [Fact]
    public void IssuerIsTheAppId()
    {
        using var key = TestKeys.CreateKey();
        var jwt = new AppJwtFactory(
            new StubPrivateKeySource(key),
            new TestTimeProvider(Now),
            987654
        ).Create();

        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Value.Split('.')[1]));

        Assert.Equal(987654, payload.RootElement.GetProperty("iss").GetInt64());
    }

    [Fact]
    public void ReadsTheKeyForEveryTokenSoRotationNeedsNoRestart()
    {
        using var key = TestKeys.CreateKey();
        StubPrivateKeySource source = new(key);
        AppJwtFactory factory = new(source, new TestTimeProvider(Now), 1);

        factory.Create();
        factory.Create();

        Assert.Equal(2, source.LoadCount);
    }

    [Fact]
    public void ToStringRedactsTheToken()
    {
        using var key = TestKeys.CreateKey();
        var jwt = new AppJwtFactory(
            new StubPrivateKeySource(key),
            new TestTimeProvider(Now),
            1
        ).Create();

        var rendered = jwt.ToString();

        Assert.DoesNotContain(jwt.Value, rendered, StringComparison.Ordinal);
        Assert.Contains("redacted", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANonPositiveAppId()
    {
        using var key = TestKeys.CreateKey();
        StubPrivateKeySource source = new(key);

        Assert.Throws<ArgumentOutOfRangeException>(() => new AppJwtFactory(source, new TestTimeProvider(Now), 0));
    }
}
