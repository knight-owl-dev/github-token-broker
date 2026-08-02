using System.Diagnostics;
using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;

/// <summary>
/// Launches a child process directly, never through a shell.
/// </summary>
/// <remarks>
/// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, so no quoting or
/// escaping is performed on a command string and no argument can be reinterpreted as
/// syntax. Standard streams are inherited rather than redirected, which keeps the
/// child interactive and keeps a token out of any buffer this process owns.
/// </remarks>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc/>
    public int Run(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentOverrides
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environmentOverrides);

        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((var name, var value) in environmentOverrides)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start \"{executablePath}\".");

        child.WaitForExit();
        return child.ExitCode;
    }
}
