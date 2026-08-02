namespace KnightOwl.GitHubTokenBroker.Cli.Application.Ports;

/// <summary>
/// Runs a child process with an explicit argument list and environment. Abstracted
/// so the <c>gh</c> use case can be tested without launching anything.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>Runs a child to completion with this process's streams attached.</summary>
    /// <param name="executablePath">Absolute path to the executable.</param>
    /// <param name="arguments">Arguments passed individually, never as one string.</param>
    /// <param name="environmentOverrides">
    /// Variables to set in the child. A <see langword="null"/> value removes an
    /// inherited variable.
    /// </param>
    /// <returns>The child's exit status.</returns>
    int Run(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentOverrides
    );
}
