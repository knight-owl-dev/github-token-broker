using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;


namespace KnightOwl.GitHubTokenBroker.Service.Tests;

/// <summary>
/// What the composition root settles that no other test reaches.
/// </summary>
public sealed class BrokerServicesTests
{
    private const string Configuration = """
        {
          "github_host": "github.com",
          "app_id": 123456,
          "installation_id": 789012,
          "private_key_path": "/keys/app.pem",
          "listen": { "unix_socket": { "path": "/run/broker.sock" } },
          "repositories": {
            "example-owner/example-repo": {
              "permissions": { "contents": "write" }
            }
          }
        }
        """;

    /// <remarks>
    /// Stated as the relation rather than the number, so it fails however that goes
    /// wrong: a drain lowered under the mint, a mint raised over the drain, or the wiring
    /// dropped and the framework's five seconds left in its place.
    /// </remarks>
    [Fact]
    public void DrainsLongEnoughToFinishTheSlowestMint()
    {
        ServiceCollection services = new();
        services.AddBrokerServices(BrokerConfiguration.FromJson(Configuration));

        using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.True(
            host.ShutdownTimeout >= BrokerProtocol.MintAttemptTimeout + BrokerProtocol.MintRetryBudget,
            $"a stop drains for {host.ShutdownTimeout}, and a mint may run "
            + $"{BrokerProtocol.MintAttemptTimeout + BrokerProtocol.MintRetryBudget}"
        );
    }
}
