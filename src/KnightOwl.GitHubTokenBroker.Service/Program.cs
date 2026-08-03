using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service;
using KnightOwl.GitHubTokenBroker.Service.Api;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


// CA1031: every catch here is the last frame before the runtime, where catching
// broadly is the point. A service manager reads the status, and a stack trace is
// not one of them. File-scoped, since that is true of the whole entry point.
#pragma warning disable CA1031

BrokerConfiguration configuration;
WebApplication app;

try
{
    configuration = BrokerConfiguration.Load(BrokerCommandLine.ParseConfigurationPath(args));

    // Environment name pinned rather than read: Development installs the developer
    // exception page, which answers a malformed request with a stack trace where
    // every other refusal is one status and one message.
    var builder = WebApplication.CreateSlimBuilder(
        new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        }
    );

    builder.Services.AddBrokerServices(configuration);
    builder.WebHost.UseBrokerListeners(configuration.Listen);

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
catch (Exception exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, "internal error", exception);
    return BrokerExitCode.Internal;
}

if (await app.VerifyPrivateKeyAsync() is { } keyFailure)
{
    return keyFailure;
}

app.MapBrokerApi();

if (await app.StartServingAsync(configuration.Listen) is { } listenerFailure)
{
    return listenerFailure;
}

BrokerHostLog.Ready(
    app.Logger,
    configuration.AppId,
    configuration.Allowlist.Count,
    configuration.Allowlist.InstallationCount
);

try
{
    await app.WaitForShutdownAsync();
    return BrokerExitCode.Success;
}
catch (Exception exception)
{
    await DiagnosticReport.WriteAsync(Console.Error, "internal error", exception);
    return BrokerExitCode.Internal;
}
