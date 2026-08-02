using KnightOwl.GitHubTokenBroker.Cli;
using KnightOwl.GitHubTokenBroker.Cli.Application;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;


if (args.Length == 0)
{
    await Console.Error.WriteLineAsync(CliCommands.Usage);
    return CliExitCode.Usage;
}

// version answers before any configuration is read, so it works on a host where the
// broker endpoint is not set yet.
if (args[0] == "version")
{
    await Console.Out.WriteLineAsync(CliCommands.ResolveVersion());
    return CliExitCode.Success;
}

if (!ClientOptions.TryRead(Environment.GetEnvironmentVariable, out var options, out var optionsError))
{
    await Console.Error.WriteLineAsync($"{Globals.AppName}: {optionsError}");
    return CliExitCode.Usage;
}

// CA1031: the last frame before the runtime, where catching broadly is the point.
#pragma warning disable CA1031
try
{
    using var broker = HttpBrokerClient.Create(options.Endpoint);

    return await broker.DispatchAsync(options, args);
}
catch (Exception exception)
{
    // Git spawns the helper per remote operation, so an escaping exception would
    // answer it with a stack trace and a status outside the documented set.
    await DiagnosticReport.WriteAsync(Console.Error, Globals.AppName, exception);
    return CliExitCode.Internal;
}
#pragma warning restore CA1031
