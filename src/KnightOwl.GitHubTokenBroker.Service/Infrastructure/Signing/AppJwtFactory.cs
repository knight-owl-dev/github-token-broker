using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// Signs RS256 GitHub App JWTs directly with <see cref="RSA"/>, avoiding a JWT
/// dependency for a two-segment token with three claims.
/// </summary>
public sealed class AppJwtFactory : IAppJwtFactory
{
    /// <summary>
    /// How far <c>iat</c> is backdated. GitHub rejects a token whose issue time is
    /// in the future, so this absorbs clock skew between here and GitHub.
    /// </summary>
    private static readonly TimeSpan IssuedAtBackdate = TimeSpan.FromSeconds(60);

    /// <summary>
    /// JWT lifetime. GitHub's ceiling is ten minutes; nine leaves room for the
    /// backdating above to stay inside it.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(9);

    private static readonly byte[] Header =
        Encoding.ASCII.GetBytes("""{"alg":"RS256","typ":"JWT"}""");

    private readonly IPrivateKeySource _privateKeySource;
    private readonly TimeProvider _timeProvider;
    private readonly long _appId;

    /// <summary>Creates the factory.</summary>
    /// <param name="privateKeySource">Supplies the signing key.</param>
    /// <param name="timeProvider">Clock for the <c>iat</c> and <c>exp</c> claims.</param>
    /// <param name="appId">The App identity used as <c>iss</c>.</param>
    public AppJwtFactory(
        IPrivateKeySource privateKeySource,
        TimeProvider timeProvider,
        long appId
    )
    {
        ArgumentNullException.ThrowIfNull(privateKeySource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appId);

        _privateKeySource = privateKeySource;
        _timeProvider = timeProvider;
        _appId = appId;
    }

    /// <inheritdoc/>
    public AppJwt Create()
    {
        var now = _timeProvider.GetUtcNow();
        var issuedAt = now.Subtract(IssuedAtBackdate).ToUnixTimeSeconds();
        var expiresAt = now.Add(Lifetime).ToUnixTimeSeconds();

        // Written directly so claim order and formatting are fixed, which keeps the
        // signed bytes reproducible for tests.
        var payload = Encoding.ASCII.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"iat":{{issuedAt}},"exp":{{expiresAt}},"iss":{{_appId}}}"""
            )
        );

        var signingInput = string.Concat(
            Base64Url.EncodeToString(Header),
            ".",
            Base64Url.EncodeToString(payload)
        );

        using var material = _privateKeySource.Load();
        var signature = material.Key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        return new AppJwt(
            string.Concat(signingInput, ".", Base64Url.EncodeToString(signature)),
            material.KeyId
        );
    }
}
