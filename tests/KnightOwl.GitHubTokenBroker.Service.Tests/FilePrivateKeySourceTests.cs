using System.Security.Cryptography;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

public sealed class FilePrivateKeySourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hga-keys").FullName;

    public void Dispose()
        => Directory.Delete(_root, recursive: true);

    private string Write(string name, string contents)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void LoadsAPkcs1Key()
    {
        using var original = TestKeys.CreateKey();
        var path = Write("app.pem", original.ExportRSAPrivateKeyPem());

        using var material = new FilePrivateKeySource(path).Load();

        Assert.Equal(
            original.ExportRSAPublicKeyPem(),
            material.Key.ExportRSAPublicKeyPem()
        );
    }

    [Fact]
    public void LoadsAPkcs8Key()
    {
        using var original = TestKeys.CreateKey();
        var path = Write("app8.pem", original.ExportPkcs8PrivateKeyPem());

        using var material = new FilePrivateKeySource(path).Load();

        Assert.Equal(
            original.ExportRSAPublicKeyPem(),
            material.Key.ExportRSAPublicKeyPem()
        );
    }

    [Fact]
    public void KeyIdDerivesFromThePublicHalfAndIsStable()
    {
        using var original = TestKeys.CreateKey();
        var path = Write("stable.pem", original.ExportRSAPrivateKeyPem());
        FilePrivateKeySource source = new(path);

        using var first = source.Load();
        using var second = source.Load();

        Assert.Equal(first.KeyId, second.KeyId);

        // A safe-to-log identifier must not be the key itself.
        Assert.DoesNotContain("PRIVATE", first.KeyId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(16, first.KeyId.Length);
    }

    [Fact]
    public void DifferentKeysHaveDifferentIdentifiers()
    {
        using var first = TestKeys.CreateKey();
        using var second = TestKeys.CreateAlternateKey();

        using var firstMaterial =
            new FilePrivateKeySource(Write("one.pem", first.ExportRSAPrivateKeyPem())).Load();

        using var secondMaterial =
            new FilePrivateKeySource(Write("two.pem", second.ExportRSAPrivateKeyPem())).Load();

        Assert.NotEqual(firstMaterial.KeyId, secondMaterial.KeyId);
    }

    [Fact]
    public void RereadsTheFileSoReplacingItRotatesTheKey()
    {
        using var before = TestKeys.CreateKey();
        using var after = TestKeys.CreateAlternateKey();
        var path = Write("rotating.pem", before.ExportRSAPrivateKeyPem());
        FilePrivateKeySource source = new(path);

        string firstKeyId;
        using (var material = source.Load())
        {
            firstKeyId = material.KeyId;
        }

        File.WriteAllText(path, after.ExportRSAPrivateKeyPem());

        using var rotated = source.Load();

        Assert.NotEqual(firstKeyId, rotated.KeyId);
    }

    [Fact]
    public void ReportsAMissingFileByPath()
    {
        var path = Path.Combine(_root, "absent.pem");

        var failure =
            Assert.Throws<ConfigurationException>(() => new FilePrivateKeySource(path).Load());

        Assert.Contains(path, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nnot base64\n-----END RSA PRIVATE KEY-----")]
    public void RejectsContentThatIsNotAnRsaKey(string contents)
    {
        var path = Write("bad.pem", contents);

        var failure =
            Assert.Throws<ConfigurationException>(() => new FilePrivateKeySource(path).Load());

        // The diagnostic names the path and never quotes the file.
        Assert.Contains(path, failure.Message, StringComparison.Ordinal);
        if (contents.Length > 0)
        {
            Assert.DoesNotContain(contents, failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RejectsAnEcKeyRatherThanSigningWithIt()
    {
        using var ec = ECDsa.Create();
        var path = Write("ec.pem", ec.ExportECPrivateKeyPem());

        Assert.Throws<ConfigurationException>(() => new FilePrivateKeySource(path).Load());
    }
}
