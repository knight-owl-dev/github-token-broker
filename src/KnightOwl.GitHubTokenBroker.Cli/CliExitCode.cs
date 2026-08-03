namespace KnightOwl.GitHubTokenBroker.Cli;

/// <summary>
/// Stable exit statuses, following the conventional <c>sysexits</c> values.
/// </summary>
/// <remarks>
/// The <c>gh</c> subcommand returns the child's status verbatim instead, so a
/// value here only ever reflects a failure that happened before the child started.
/// </remarks>
public static class CliExitCode
{
    /// <summary>The command succeeded.</summary>
    public const int Success = 0;

    /// <summary>The command line or environment was wrong.</summary>
    public const int Usage = 64;

    /// <summary>An unexpected internal failure.</summary>
    public const int Internal = 70;

    /// <summary>The broker could not be reached.</summary>
    public const int Unavailable = 69;

    /// <summary>The broker was reached but GitHub was not, so retrying can help.</summary>
    /// <remarks>
    /// Apart from <see cref="Unavailable"/>, which is the broker itself: here it is
    /// running and configured, and GitHub is what is missing.
    /// </remarks>
    public const int Temporary = 75;

    /// <summary>The broker refused the repository.</summary>
    public const int NotAuthorized = 77;

    /// <summary>
    /// The broker and GitHub disagree about what it may mint, so retrying cannot help.
    /// </summary>
    public const int Configuration = 78;
}
