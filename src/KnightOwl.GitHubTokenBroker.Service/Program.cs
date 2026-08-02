using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service;
using KnightOwl.GitHubTokenBroker.Service.Api;
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

app.UseClientCredentialGate();
app.MapBrokerApi();
app.ApplyUnixSocketModeOnStart(configuration.Listen.UnixSocket);

BrokerHostLog.Ready(
    app.Logger,
    configuration.AppId,
    configuration.InstallationId,
    configuration.Allowlist.Count
);

await app.RunAsync();
return BrokerExitCode.Success;
