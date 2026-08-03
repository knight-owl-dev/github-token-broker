using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class BrokerConfigurationTests
{
    private const string Valid = """
        {
          "github_host": "github.com",
          "app_id": 123456,
          "installation_id": 789012,
          "private_key_path": "/keys/app.pem",
          "listen": { "unix_socket": { "path": "/run/broker.sock" } },
          "repositories": {
            "example-owner/example-repo": {
              "permissions": { "contents": "write", "checks": "read" }
            }
          }
        }
        """;

    [Fact]
    public void AcceptsAMinimalValidDocument()
    {
        var configuration = BrokerConfiguration.FromJson(Valid);

        Assert.Equal(GitHubHost.GitHubCom, configuration.Host);
        Assert.Equal(123456, configuration.AppId);
        Assert.Equal(789012, configuration.DefaultInstallation.Value);
        Assert.Equal("/keys/app.pem", configuration.PrivateKeyPath);
        Assert.Equal("/run/broker.sock", configuration.Listen.UnixSocket.Path);
        Assert.Equal(new Uri(BrokerConfiguration.DefaultApiUrl), configuration.ApiBaseUri);
        Assert.Equal(BrokerConfiguration.DefaultRefreshMargin, configuration.RefreshMargin);
        Assert.Equal(1, configuration.Allowlist.Count);
    }

    [Fact]
    public void RejectsAnUnknownMember()
    {
        // An unrecognized security-sensitive key must fail rather than be ignored.
        var failure = Assert.Throws<ConfigurationException>(() => BrokerConfiguration.FromJson(
                """
                {
                  "app_id": 1,
                  "installation_id": 2,
                  "private_key_path": "/k.pem",
                  "allow_everything": true,
                  "listen": { "unix_socket": { "path": "/s.sock" } },
                  "repositories": { "o/r": { "permissions": { "contents": "read" } } }
                }
                """
            )
        );

        Assert.Contains("schema", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{ "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    [InlineData(
        """{ "app_id": 1, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsAMissingRequiredMember(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 0, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    [InlineData(
        """{ "app_id": 1, "installation_id": -2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsANonPositiveIdentifier(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "app.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsARelativePrivateKeyPath(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsAListenSectionWithNoTransport(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "broker.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsARelativeSocketPath(string json)
        => Rejects(json);

    /// <param name="json">A configuration document.</param>
    /// <remarks>
    /// An allowlist that is empty and one that is absent are the same mistake.
    /// </remarks>
    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { } }"""
    )]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } } }"""
    )]
    public void RejectsAnAllowlistThatServesNothing(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { } } } }"""
    )]
    public void RejectsAnAllowlistEntryWithNoPermissions(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "workflows": "write" } } } }"""
    )]
    public void RejectsAPermissionThisBrokerNeverRequests(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "github_host": "github.example.com", "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsAnUnsupportedHost(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "https://github.com/o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsAMalformedRepositoryKey(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "api_url": "http://api.github.com", "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsPlaintextBeyondLoopback(string json)
        => Rejects(json);

    [Theory]
    [InlineData(
        """{ "token_refresh_margin_seconds": 5, "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    [InlineData(
        """{ "token_refresh_margin_seconds": 3600, "app_id": 1, "installation_id": 2, "private_key_path": "/k.pem", "listen": { "unix_socket": { "path": "/s.sock" } }, "repositories": { "o/r": { "permissions": { "contents": "read" } } } }"""
    )]
    public void RejectsARefreshMarginOutsideItsBounds(string json)
        => Rejects(json);

    /// <remarks>
    /// The transport this replaced. It fails as an unrecognized member rather
    /// than being ignored, so an old document stops the broker instead of
    /// silently binding nothing.
    /// </remarks>
    [Fact]
    public void RejectsAConfigurationStillNamingTcp()
    {
        var failure = Assert.Throws<ConfigurationException>(() => BrokerConfiguration.FromJson(
                """
                {
                  "app_id": 1,
                  "installation_id": 2,
                  "private_key_path": "/k.pem",
                  "listen": {
                    "unix_socket": { "path": "/s.sock" },
                    "tcp": { "address": "127.0.0.1", "port": 8765 }
                  },
                  "repositories": { "o/r": { "permissions": { "contents": "read" } } }
                }
                """
            )
        );

        Assert.Contains("schema", failure.Message, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Reaches the override through the document rather than through
    /// <c>AllowlistValidator</c>, which takes entries already deserialized and so
    /// cannot show that <c>installation_id</c> maps onto the member reading it.
    /// </remarks>
    [Fact]
    public void ReadsAPerRepositoryInstallationOverride()
    {
        var configuration = BrokerConfiguration.FromJson(
            """
            {
              "app_id": 123456,
              "installation_id": 789012,
              "private_key_path": "/keys/app.pem",
              "listen": { "unix_socket": { "path": "/run/broker.sock" } },
              "repositories": {
                "example-owner/example-repo": {
                  "permissions": { "contents": "write" }
                },
                "other-owner/their-repo": {
                  "installation_id": 345678,
                  "permissions": { "contents": "read" }
                }
              }
            }
            """
        );

        Assert.True(
            configuration.Allowlist.TryResolve(
                RepositoryName.Parse("example-owner/example-repo"),
                out var fallback
            )
        );

        Assert.Equal(789012, fallback.Installation.Value);

        Assert.True(
            configuration.Allowlist.TryResolve(
                RepositoryName.Parse("other-owner/their-repo"),
                out var overridden
            )
        );

        Assert.Equal(345678, overridden.Installation.Value);
    }

    /// <param name="member">A near miss for the override's name.</param>
    /// <remarks>
    /// Unmapped members are refused inside a repository entry as well as at the
    /// top level, so a spelling that is nearly right stops the broker rather than
    /// leaving the entry on the default installation.
    /// </remarks>
    [Theory]
    [InlineData("installationId")]
    [InlineData("installation")]
    public void RejectsAMisspelledInstallationOverride(string member)
        => Rejects(
            $$"""
            {
              "app_id": 1,
              "installation_id": 2,
              "private_key_path": "/k.pem",
              "listen": { "unix_socket": { "path": "/s.sock" } },
              "repositories": {
                "o/r": { "{{member}}": 3, "permissions": { "contents": "read" } }
              }
            }
            """
        );

    private static void Rejects(string json)
        => Assert.Throws<ConfigurationException>(() => BrokerConfiguration.FromJson(json));

    [Fact]
    public void RejectsTwoAllowlistEntriesDifferingOnlyByCase()
    {
        // Both spellings resolve to one identity, so accepting them would leave two
        // ceilings competing for the same repository.
        var failure = Assert.Throws<ConfigurationException>(() => BrokerConfiguration.FromJson(
                """
                {
                  "app_id": 1,
                  "installation_id": 2,
                  "private_key_path": "/k.pem",
                  "listen": { "unix_socket": { "path": "/s.sock" } },
                  "repositories": {
                    "Owner/Repo": { "permissions": { "contents": "write" } },
                    "owner/repo": { "permissions": { "contents": "read" } }
                  }
                }
                """
            )
        );

        Assert.Contains("more than once", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsASocketPathBeyondThePlatformLimit()
    {
        var tooLong = "/" + new string('a', 200);

        var failure = Assert.Throws<ConfigurationException>(() => BrokerConfiguration.FromJson(
                $$"""
                {
                  "app_id": 1,
                  "installation_id": 2,
                  "private_key_path": "/k.pem",
                  "listen": { "unix_socket": { "path": "{{tooLong}}" } },
                  "repositories": { "o/r": { "permissions": { "contents": "read" } } }
                }
                """
            )
        );

        Assert.Contains("bytes", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsLoopbackPlaintextAsATestingSeam()
    {
        var configuration = BrokerConfiguration.FromJson(
            """
            {
              "api_url": "http://127.0.0.1:8123",
              "app_id": 1,
              "installation_id": 2,
              "private_key_path": "/k.pem",
              "listen": { "unix_socket": { "path": "/s.sock" } },
              "repositories": { "o/r": { "permissions": { "contents": "read" } } }
            }
            """
        );

        Assert.Equal(new Uri("http://127.0.0.1:8123"), configuration.ApiBaseUri);
    }

    [Fact]
    public void AllowlistResolvesRegardlessOfRequestCasing()
    {
        var configuration = BrokerConfiguration.FromJson(Valid);

        Assert.True(
            configuration.Allowlist.TryResolve(
                RepositoryName.Parse("EXAMPLE-OWNER/EXAMPLE-REPO"),
                out var policy
            )
        );

        Assert.Equal("checks:read;contents:write", policy.Ceiling.CanonicalForm);
    }

    [Fact]
    public void UnlistedRepositoryDoesNotResolve()
    {
        var configuration = BrokerConfiguration.FromJson(Valid);

        Assert.False(configuration.Allowlist.TryResolve(RepositoryName.Parse("other/upstream"), out _));
    }

    [Fact]
    public void GrantKeyBindsRepositoryAndCeilingTogether()
    {
        var repository = RepositoryName.Parse("owner/repo");
        var installation = TestInstallations.Installation();

        RepositoryAccessPolicy read = new(repository, installation, PermissionSetFor("read"));
        RepositoryAccessPolicy write = new(repository, installation, PermissionSetFor("write"));

        Assert.NotEqual(read.GrantKey, write.GrantKey);
    }

    [Fact]
    public void GrantKeyBindsTheInstallationToo()
    {
        var repository = RepositoryName.Parse("owner/repo");
        var ceiling = PermissionSetFor("write");

        RepositoryAccessPolicy here = new(repository, TestInstallations.Installation(), ceiling);
        RepositoryAccessPolicy elsewhere = new(repository, TestInstallations.Installation(345678), ceiling);

        Assert.NotEqual(here.GrantKey, elsewhere.GrantKey);
    }

    private static Domain.Permissions.PermissionSet PermissionSetFor(string level)
    {
        Assert.True(
            Domain.Permissions.PermissionSet.TryCreate(
                [new KeyValuePair<string, string>("contents", level)],
                out var set,
                out _
            )
        );

        return set;
    }

    [Fact]
    public void LoadRequiresAnAbsolutePath()
        => Assert.Throws<ConfigurationException>(() => BrokerConfiguration.Load("relative.json"));

    [Fact]
    public void LoadReportsAMissingFileWithoutLeakingContents()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"hga-missing-{Guid.NewGuid():N}.json");

        var failure =
            Assert.Throws<ConfigurationException>(() => BrokerConfiguration.Load(missing));

        Assert.Contains(missing, failure.Message, StringComparison.Ordinal);
    }
}
