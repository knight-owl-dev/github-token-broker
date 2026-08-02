using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;


namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// The broker's composition root.
/// </summary>
internal static class BrokerServices
{
    /// <param name="services">The collection to register into.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Registers everything the broker serves a request with.</summary>
        /// <param name="configuration">The loaded configuration.</param>
        public void AddBrokerServices(BrokerConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

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

            services.AddSingleton<IInstallationTokenIssuer>(provider =>
                new GitHubInstallationTokenIssuer(
                    GitHubHttpClientFactory.Create(configuration.ApiBaseUri),
                    provider.GetRequiredService<IAppJwtFactory>(),
                    provider.GetRequiredService<TimeProvider>(),
                    configuration.InstallationId,
                    provider.GetRequiredService<ILogger<GitHubInstallationTokenIssuer>>()
                )
            );
        }
    }
}
