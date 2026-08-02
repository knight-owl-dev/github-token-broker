using System.Diagnostics.CodeAnalysis;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Processes;

/// <summary>
/// Finds the real GitHub CLI.
/// </summary>
/// <remarks>
/// The search deliberately skips this executable, since deployments put the wrapper
/// on <c>PATH</c> and a wrapper that found itself would recurse.
/// </remarks>
public static class GitHubCliLocator
{
    private const string ExecutableName = "gh";

    /// <summary>Resolves the GitHub CLI to launch.</summary>
    /// <param name="configuredPath">An explicit path that wins when supplied.</param>
    /// <param name="searchPath">The <c>PATH</c> value to search.</param>
    /// <param name="ownExecutablePath">This process's own executable, never selected.</param>
    /// <param name="executablePath">The resolved path when one is found.</param>
    /// <param name="error">Why no usable executable was found.</param>
    /// <returns><see langword="true"/> when a GitHub CLI was resolved.</returns>
    public static bool TryLocate(
        string? configuredPath,
        string? searchPath,
        string? ownExecutablePath,
        [NotNullWhen(true)] out string? executablePath,
        [NotNullWhen(false)] out string? error
    )
    {
        executablePath = null;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!File.Exists(configuredPath))
            {
                error = $"The configured GitHub CLI \"{configuredPath}\" does not exist.";
                return false;
            }

            if (!IsExecutable(configuredPath))
            {
                error = $"The configured GitHub CLI \"{configuredPath}\" is not executable.";
                return false;
            }

            executablePath = Path.GetFullPath(configuredPath);
            error = null;
            return true;
        }

        var own = Resolve(ownExecutablePath);

        foreach (var directory in (searchPath ?? string.Empty).Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ))
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, ExecutableName));
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is skipped rather than fatal.
                continue;
            }

            if (!File.Exists(candidate) || !IsExecutable(candidate))
            {
                continue;
            }

            // Compared after following links: a deployment that puts the wrapper on
            // PATH as a symlink named "gh" would otherwise not match, and the wrapper
            // would launch itself.
            if (own is not null
                && string.Equals(Resolve(candidate), own, StringComparison.Ordinal))
            {
                continue;
            }

            executablePath = candidate;
            error = null;
            return true;
        }

        error = $"No GitHub CLI named \"{ExecutableName}\" was found on PATH.";
        return false;
    }

    /// <summary>Reports whether a file can be executed by anyone.</summary>
    /// <param name="path">An existing file.</param>
    /// <returns><see langword="true"/> when an execute bit is set.</returns>
    /// <remarks>
    /// Launching a file without one throws from the runtime rather than returning
    /// a status, and by then a token has been minted. Which bit applies needs the
    /// owner and groups, so any of the three makes a candidate and the launch
    /// settles it.
    /// </remarks>
    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        var mode = File.GetUnixFileMode(path);
        return mode.HasFlag(UnixFileMode.UserExecute)
            || mode.HasFlag(UnixFileMode.GroupExecute)
            || mode.HasFlag(UnixFileMode.OtherExecute);
    }

    /// <summary>Resolves a path to what it finally points at.</summary>
    /// <param name="path">The path to resolve, which may be a link or absent.</param>
    /// <returns>The final target, or <see langword="null"/> when there is no path.</returns>
    private static string? Resolve(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path);

        try
        {
            return File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
        }
        catch (IOException)
        {
            // A broken or cyclic link resolves to nothing useful; the unresolved path
            // is still worth comparing.
            return full;
        }
    }
}
