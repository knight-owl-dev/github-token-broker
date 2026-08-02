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

            if (!File.Exists(candidate))
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
