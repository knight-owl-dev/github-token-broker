using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Resilience;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;


namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// The broker's composition root.
/// </summary>
internal static class BrokerServices
{
    /// <summary>
    /// How long to wait between mint attempts. An immediate retry tends to reach
    /// whatever answered the first one.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);


    /// <param name="services">The collection to register into.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Registers everything the broker serves a request with.</summary>
        /// <param name="configuration">The loaded configuration.</param>
        public void AddBrokerServices(BrokerConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            // Without this the framework default cuts off a mint the retry budget is
            // still spending.
            services.Configure<HostOptions>(host => host.ShutdownTimeout = BrokerProtocol.ShutdownDrain);

            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(configuration);
            services.AddSingleton(configuration.Allowlist);
            services.AddSingleton(configuration.Host);
            services.AddSingleton<IPrivateKeySource>(new FilePrivateKeySource(configuration.PrivateKeyPath));

            services.AddSingleton<IAppJwtFactory>(provider => new AppJwtFactory(
                    provider.GetRequiredService<IPrivateKeySource>(),
                    provider.GetRequiredService<TimeProvider>(),
                    configuration.AppId
                )
            );

            services.AddSingleton(provider => new RepositoryRequestValidator(
                    provider.GetRequiredService<GitHubHost>(),
                    provider.GetRequiredService<RepositoryAllowlist>()
                )
            );

            services.AddSingleton(provider => new InstallationTokenCache(
                    provider.GetRequiredService<TimeProvider>(),
                    configuration.RefreshMargin
                )
            );

            services.AddSingleton<TokenIssuingService>();

            // Retry wraps the issuer rather than living in it, so the issuer stays the
            // one component that reaches GitHub.
            services.AddSingleton<IInstallationTokenIssuer>(provider =>
                new RetryingTokenIssuer(
                    new GitHubInstallationTokenIssuer(
                        GitHubHttpClientFactory.Create(configuration.ApiBaseUri),
                        provider.GetRequiredService<IAppJwtFactory>(),
                        provider.GetRequiredService<TimeProvider>(),
                        provider.GetRequiredService<ILogger<GitHubInstallationTokenIssuer>>()
                    ),
                    new RetryBudget(
                        provider.GetRequiredService<TimeProvider>(),
                        BrokerProtocol.MintRetryBudget,
                        RetryDelay
                    ),
                    provider.GetRequiredService<ILogger<RetryingTokenIssuer>>()
                )
            );
        }
    }
}
