using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using KnightOwl.GitHubTokenBroker.Cli;
using KnightOwl.GitHubTokenBroker.Cli.Application;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


const string usage = $"""
    usage:
      {Globals.AppName} credential [get|store|erase]
      {Globals.AppName} gh OWNER/REPOSITORY -- GH_ARGUMENT...
      {Globals.AppName} check OWNER/REPOSITORY
      {Globals.AppName} version
    """;

if (args.Length == 0)
{
    Console.Error.WriteLine(usage);
    return CliExitCode.Usage;
}

// version answers before any configuration is read, so it works on a host where the
// broker endpoint is not set yet.
if (args[0] == "version")
{
    Console.Out.WriteLine(ResolveVersion());
    return CliExitCode.Success;
}

if (!ClientOptions.TryRead(Environment.GetEnvironmentVariable, out var options, out var optionsError))
{
    Console.Error.WriteLine($"{Globals.AppName}: {optionsError}");
    return CliExitCode.Usage;
}

using var broker = HttpBrokerClient.Create(options.Endpoint, options.ClientCredential);

switch (args[0])
{
    case "credential":
        if (args.Length > 2)
        {
            Console.Error.WriteLine(usage);
            return CliExitCode.Usage;
        }

        return await new CredentialHelper(broker, Console.In, Console.Out, Console.Error)
            .RunAsync(args.Length == 2 ? args[1] : null, CancellationToken.None);

    case "check":
        if (args.Length != 2)
        {
            Console.Error.WriteLine(usage);
            return CliExitCode.Usage;
        }

        if (!TryParseRepository(args[1], out var checkRepository))
        {
            return CliExitCode.Usage;
        }

        return await new AuthorizationCheck(broker, Console.Out, Console.Error)
            .RunAsync(checkRepository, CancellationToken.None);

    case "gh":
        // Requires OWNER/REPOSITORY, a literal --, and at least one argument, so the
        // repository a token is minted for is always explicit rather than inferred
        // from the working directory.
        if (args.Length < 4 || args[2] != "--")
        {
            Console.Error.WriteLine(usage);
            return CliExitCode.Usage;
        }

        if (!TryParseRepository(args[1], out var ghRepository))
        {
            return CliExitCode.Usage;
        }

        return await new GitHubCliRunner(
                broker,
                new ProcessLauncher(),
                Environment.GetEnvironmentVariable,
                options.GitHubCliPath,
                Environment.ProcessPath,
                Console.Error
            )
            .RunAsync(ghRepository, args[3..], CancellationToken.None);

    default:
        Console.Error.WriteLine(usage);
        return CliExitCode.Usage;
}

static bool TryParseRepository(
    string value,
    [NotNullWhen(true)] out RepositoryName? repository
)
{
    if (RepositoryName.TryParse(value, out repository, out var error))
    {
        return true;
    }

    Console.Error.WriteLine($"{Globals.AppName}: {error}");
    return false;
}

static string ResolveVersion()
{
    var informational = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (string.IsNullOrEmpty(informational))
    {
        return "unknown";
    }

    // Strip the source-revision suffix the SDK appends.
    var plus = informational.IndexOf('+', StringComparison.Ordinal);
    return plus < 0 ? informational : informational[..plus];
}
