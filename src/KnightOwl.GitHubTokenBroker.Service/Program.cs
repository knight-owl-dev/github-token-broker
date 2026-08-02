using System.Net;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service;
using KnightOwl.GitHubTokenBroker.Service.Api;
using KnightOwl.GitHubTokenBroker.Service.Application;
using KnightOwl.GitHubTokenBroker.Service.Application.Ports;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.GitHub;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


BrokerConfiguration configuration;
try
{
    configuration = BrokerConfiguration.Load(ParseConfigurationPath(args));
}
catch (BrokerUsageException exception)
{
    DiagnosticReport.Write(Console.Error, null, exception);
    Console.Error.WriteLine("usage: github-token-broker --config ABSOLUTE_CONFIG_PATH");
    return BrokerExitCode.Usage;
}
catch (ConfigurationException exception)
{
    DiagnosticReport.Write(Console.Error, "configuration error", exception);
    return BrokerExitCode.Configuration;
}

var builder = WebApplication.CreateSlimBuilder();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(configuration.Allowlist);
builder.Services.AddSingleton(configuration.Host);
builder.Services.AddSingleton<IPrivateKeySource>(new FilePrivateKeySource(configuration.PrivateKeyPath));
builder.Services.AddSingleton<IAppJwtFactory>(services => new AppJwtFactory(
        services.GetRequiredService<IPrivateKeySource>(),
        services.GetRequiredService<TimeProvider>(),
        configuration.AppId
    )
);

builder.Services.AddSingleton(services => new RepositoryRequestValidator(
        services.GetRequiredService<GitHubHost>(),
        services.GetRequiredService<RepositoryAllowlist>()
    )
);

builder.Services.AddSingleton(services => new InstallationTokenCache(
        services.GetRequiredService<TimeProvider>(),
        configuration.RefreshMargin
    )
);

builder.Services.AddSingleton<TokenIssuingService>();

builder.Services.AddSingleton<IInstallationTokenIssuer>(services =>
    new GitHubInstallationTokenIssuer(
        CreateGitHubClient(configuration),
        services.GetRequiredService<IAppJwtFactory>(),
        services.GetRequiredService<TimeProvider>(),
        configuration.InstallationId,
        services.GetRequiredService<ILogger<GitHubInstallationTokenIssuer>>()
    )
);

try
{
    if (configuration.Listen.Tcp is { } tcpOptions)
    {
        builder.Services.AddSingleton(ClientCredential.Load(tcpOptions.ClientCredentialPath));
    }

    ConfigureListeners(builder.WebHost, configuration);
}
catch (ConfigurationException exception)
{
    DiagnosticReport.Write(Console.Error, "configuration error", exception);
    return BrokerExitCode.Configuration;
}

WebApplication app;
try
{
    // Kestrel applies ConfigureKestrel here rather than when it was registered, so a
    // rejected listener surfaces at this point.
    app = builder.Build();
}
catch (ConfigurationException exception)
{
    DiagnosticReport.Write(Console.Error, "configuration error", exception);
    return BrokerExitCode.Configuration;
}

app.UseClientCredentialGate();
app.MapBrokerApi();

if (configuration.Listen.UnixSocket is { } socket)
{
    // The socket file exists only once the listener does, so the mode is applied
    // here. Failing stops the broker: this transport cannot serve on a socket
    // whose permissions are unknown.
    var socketMode = UnixSocketPreparation.Format(socket.Mode);
    app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(socket.Path, socket.Mode);
                }

                BrokerStartupLog.UnixSocketReady(app.Logger, socket.Path, socketMode);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                BrokerStartupLog.UnixSocketModeFailed(
                    app.Logger,
                    socket.Path,
                    socketMode,
                    exception
                );

                app.Lifetime.StopApplication();
            }
        }
    );
}

BrokerStartupLog.Ready(
    app.Logger,
    configuration.AppId,
    configuration.InstallationId,
    configuration.Allowlist.Count
);

await app.RunAsync();
return BrokerExitCode.Success;

static string ParseConfigurationPath(string[] arguments)
    => arguments is not ["--config", _]
        ? throw new BrokerUsageException("Expected exactly --config ABSOLUTE_CONFIG_PATH.")
        : arguments[1];

static void ConfigureListeners(IWebHostBuilder webHost, BrokerConfiguration configuration)
{
    if (configuration.Listen.UnixSocket is { } unixSocket)
    {
        UnixSocketPreparation.Prepare(unixSocket.Path);
    }

    webHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = BrokerProtocol.MaxRequestBytes;
            options.AddServerHeader = false;

            if (configuration.Listen.UnixSocket is { } socket)
            {
                options.ListenUnixSocket(socket.Path);
            }

            if (configuration.Listen.Tcp is { } tcp)
            {
                options.Listen(
                    tcp.Address,
                    tcp.Port,
                    listen => listen.Use(next => async connection =>
                        {
                            // Marks the connection so the credential gate can require a secret
                            // here without imposing one on the Unix socket.
                            connection.Features.Set(new TcpTransportMarker());
                            await next(connection);
                        }
                    )
                );
            }
        }
    );
}

static HttpClient CreateGitHubClient(BrokerConfiguration configuration)
{
    // One long-lived client for one host. PooledConnectionLifetime keeps DNS from
    // going stale in a process that runs for weeks.
    SocketsHttpHandler handler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    // A trailing slash keeps a relative request path from replacing the last
    // segment of a configured API path.
    var baseUrl = configuration.ApiBaseUri.AbsoluteUri;

    HttpClient client = new(handler)
    {
        BaseAddress = baseUrl.EndsWith('/') ? new Uri(baseUrl) : new Uri($"{baseUrl}/"),

        // Bounds the wait so a hung request cannot hold a coalesced mint open indefinitely.
        Timeout = TimeSpan.FromSeconds(15),
    };

    // GitHub requires a User-Agent.
    client.DefaultRequestHeaders.UserAgent.ParseAdd("github-token-broker");
    return client;
}
