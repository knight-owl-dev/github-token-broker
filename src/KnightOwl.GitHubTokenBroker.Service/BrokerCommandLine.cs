using KnightOwl.GitHubTokenBroker.Service.Application;


namespace KnightOwl.GitHubTokenBroker.Service;

/// <summary>
/// The broker's command line: one option, and the line printed when it is wrong.
/// </summary>
internal static class BrokerCommandLine
{
    /// <summary>What the broker accepts, for the operator who got it wrong.</summary>
    public const string Usage = $"usage: {Globals.AppName} --config ABSOLUTE_CONFIG_PATH";

    /// <summary>Reads the configuration path from the command line.</summary>
    /// <param name="arguments">The process arguments.</param>
    /// <returns>The path to load configuration from.</returns>
    /// <exception cref="BrokerUsageException">The arguments are anything else.</exception>
    public static string ParseConfigurationPath(string[] arguments)
        => arguments is not ["--config", _]
            ? throw new BrokerUsageException("Expected exactly --config ABSOLUTE_CONFIG_PATH.")
            : arguments[1];
}
