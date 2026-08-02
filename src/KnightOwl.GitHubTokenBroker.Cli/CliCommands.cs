using System.Reflection;
using KnightOwl.GitHubTokenBroker.Cli.Application;
using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Cli;

/// <summary>
/// What the command line selects between, and the parsing each command shares.
/// </summary>
/// <remarks>
/// Held apart from the entry point so the statements there read as a sequence of
/// guards, and so nothing here can reach back into what they built.
/// </remarks>
internal static class CliCommands
{
    /// <summary>The synopsis, printed for any command line this cannot serve.</summary>
    public const string Usage = $"""
        usage:
          {Globals.AppName} credential [get|store|erase]
          {Globals.AppName} gh OWNER/REPOSITORY -- GH_ARGUMENT...
          {Globals.AppName} check OWNER/REPOSITORY
          {Globals.AppName} version
        """;

    /// <param name="broker">Supplies tokens to whichever command needs one.</param>
    extension(IBrokerClient broker)
    {
        /// <summary>Runs the command the arguments name.</summary>
        /// <param name="options">What the environment configured.</param>
        /// <param name="arguments">The command line, its first element already checked.</param>
        /// <returns>The process exit status.</returns>
        public async Task<int> DispatchAsync(ClientOptions options, string[] arguments)
        {
            ArgumentNullException.ThrowIfNull(broker);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(arguments);

            switch (arguments[0])
            {
                case "credential":
                    if (arguments.Length > 2)
                    {
                        await Console.Error.WriteLineAsync(Usage);
                        return CliExitCode.Usage;
                    }

                    return await new CredentialHelper(broker, Console.In, Console.Out, Console.Error)
                        .RunAsync(arguments.Length == 2 ? arguments[1] : null, CancellationToken.None);

                case "check":
                    if (arguments.Length != 2)
                    {
                        await Console.Error.WriteLineAsync(Usage);
                        return CliExitCode.Usage;
                    }

                    if (await ParseRepositoryAsync(arguments[1]) is not { } checkRepository)
                    {
                        return CliExitCode.Usage;
                    }

                    return await new AuthorizationCheck(broker, Console.Out, Console.Error)
                        .RunAsync(checkRepository, CancellationToken.None);

                case "gh":
                    // Requires OWNER/REPOSITORY, a literal --, and at least one argument, so the
                    // repository a token is minted for is always explicit rather than inferred
                    // from the working directory.
                    if (arguments.Length < 4 || arguments[2] != "--")
                    {
                        await Console.Error.WriteLineAsync(Usage);
                        return CliExitCode.Usage;
                    }

                    if (await ParseRepositoryAsync(arguments[1]) is not { } ghRepository)
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
                        .RunAsync(ghRepository, arguments[3..], CancellationToken.None);

                default:
                    await Console.Error.WriteLineAsync(Usage);
                    return CliExitCode.Usage;
            }
        }
    }

    /// <summary>Reports the version this was built from.</summary>
    /// <returns>The version, or <c>unknown</c> when the attribute is absent.</returns>
    public static string ResolveVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return "unknown";
        }

        // Strip the source-revision suffix the SDK appends.
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    /// <summary>Parses a repository argument, reporting what was wrong with it.</summary>
    /// <param name="value">The argument as written.</param>
    /// <returns>The repository, or <see langword="null"/> once refused.</returns>
    private static async Task<RepositoryName?> ParseRepositoryAsync(string value)
    {
        if (RepositoryName.TryParse(value, out var repository, out var error))
        {
            return repository;
        }

        await Console.Error.WriteLineAsync($"{Globals.AppName}: {error}");
        return null;
    }
}
