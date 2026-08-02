using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service;
using KnightOwl.GitHubTokenBroker.Service.Api;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


BrokerConfiguration configuration;
WebApplication app;

try
{
    configuration = BrokerConfiguration.Load(BrokerCommandLine.ParseConfigurationPath(args));

    var builder = WebApplication.CreateSlimBuilder();
    builder.Services.AddBrokerServices(configuration);
    builder.WebHost.UseBrokerListeners(configuration.Listen);

    // Kestrel applies ConfigureKestrel here rather than when it was registered, so a
    // rejected listener surfaces at this point.
    app = builder.Build();
}
catch (BrokerUsageException exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, null, exception);
    await Console.Error.WriteLineAsync(BrokerCommandLine.Usage);
    return BrokerExitCode.Usage;
}
catch (ConfigurationException exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, "configuration error", exception);
    return BrokerExitCode.Configuration;
}

// Proven before serving, so a broker that answers /health and /v1/check can also
// mint. The per-mint read stays, and is what picks up a replacement key.
try
{
    using var privateKey = app.Services.GetRequiredService<IPrivateKeySource>().Load();
    BrokerHostLog.PrivateKeyLoaded(app.Logger, privateKey.KeyId);
}
catch (ConfigurationException exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, "private key error", exception);
    return BrokerExitCode.PrivateKey;
}

app.MapBrokerApi();

// Started rather than run, so the socket can be narrowed between binding and
// serving.
try
{
    await app.StartAsync();
}
catch (IOException exception)
{
    // Kestrel binds here rather than at build, so an occupied port reports like
    // the occupied socket path that UseBrokerListeners already refuses.
    await DiagnosticReport.WriteAsync(Console.Error, "configuration error", exception);
    return BrokerExitCode.Configuration;
}

try
{
    app.NarrowUnixSocket(configuration.Listen.UnixSocket);
}
catch (UnixSocketModeException exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, "socket error", exception);
    await app.StopAsync();
    return BrokerExitCode.SocketMode;
}

BrokerHostLog.Ready(
    app.Logger,
    configuration.AppId,
    configuration.InstallationId,
    configuration.Allowlist.Count
);

await app.WaitForShutdownAsync();
return BrokerExitCode.Success;
