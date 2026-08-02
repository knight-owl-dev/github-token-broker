using System.Security.Cryptography;


namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// Supplies in-memory RSA keys, so the suite carries no operator key or fixture
/// file.
/// </summary>
/// <remarks>
/// Generating a 2048-bit key costs far more than every assertion around it, and
/// almost no test needs a <em>distinct</em> key. Two are generated once per assembly
/// and handed out as PEM, which callers import: parsing rather than generation. PEM
/// rather than a live <see cref="RSA"/>, because an instance is not documented as
/// safe for the concurrent use that parallel test classes would give it.
/// </remarks>
internal static class TestKeys
{
    private static readonly string PrimaryPem = GeneratePem();

    private static readonly string AlternatePem = GeneratePem();

    /// <summary>An RSA key. Every call returns the same key material.</summary>
    public static RSA CreateKey()
        => Import(PrimaryPem);

    /// <summary>A second RSA key, for the tests that need two that differ.</summary>
    public static RSA CreateAlternateKey()
        => Import(AlternatePem);

    public static string ToPkcs1Pem(RSA key)
        => key.ExportRSAPrivateKeyPem();

    private static string GeneratePem()
    {
        using RSA key = RSA.Create(2048);
        return key.ExportRSAPrivateKeyPem();
    }

    private static RSA Import(string pem)
    {
        RSA key = RSA.Create();
        try
        {
            key.ImportFromPem(pem);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
