using System.Text;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Git;

/// <summary>
/// Reads and writes Git's credential-helper wire format.
/// </summary>
/// <remarks>
/// The format is <c>key=value</c> lines terminated by a blank line or end of input.
/// Nothing read here is ever logged: the input can carry a password on a
/// <c>store</c> operation.
/// </remarks>
public static class GitCredentialProtocol
{
    /// <summary>Username Git must send with an installation token.</summary>
    public const string TokenUsername = "x-access-token";

    /// <summary>Largest accepted input. Git sends a handful of short lines.</summary>
    private const int MaxInputBytes = 8192;

    /// <summary>Largest accepted number of lines, bounding a hostile writer.</summary>
    private const int MaxLines = 64;

    /// <summary>Reads the credential description Git wrote to standard input.</summary>
    /// <param name="input">The stream Git is writing to.</param>
    /// <returns>The fields this client acts on.</returns>
    /// <exception cref="InvalidDataException">The input exceeded its bounds.</exception>
    public static GitCredentialRequest Read(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string? protocol = null;
        string? host = null;
        string? path = null;
        var remaining = MaxInputBytes;
        var lines = 0;

        while (TryReadLine(input, ref remaining) is { } line)
        {
            if (line.Length == 0)
            {
                break;
            }

            if (++lines > MaxLines)
            {
                throw new InvalidDataException("The credential description was too large.");
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..];

            // Every unsupported field, including any password, is ignored.
            switch (key)
            {
                case "protocol":
                    protocol = value;
                    break;
                case "host":
                    host = value;
                    break;
                case "path":
                    path = value;
                    break;
            }
        }

        return new GitCredentialRequest(protocol, host, path);
    }

    /// <summary>
    /// Reads one line, spending from a budget shared across the whole description.
    /// </summary>
    /// <param name="input">The stream Git is writing to.</param>
    /// <param name="remaining">Characters still allowed, decremented as they are read.</param>
    /// <returns>The line, or <see langword="null"/> at end of input.</returns>
    /// <exception cref="InvalidDataException">The budget ran out mid-line.</exception>
    /// <remarks>
    /// <see cref="TextReader.ReadLine"/> would materialize a line of any length
    /// before a check on the result could run, so the cap is applied while reading
    /// rather than after.
    /// </remarks>
    private static string? TryReadLine(TextReader input, ref int remaining)
    {
        StringBuilder line = new();

        while (true)
        {
            var next = input.Read();
            switch (next)
            {
                case < 0:
                    return line.Length == 0 ? null : line.ToString();
                case '\n':
                    return line.ToString();
            }

            if (--remaining < 0)
            {
                throw new InvalidDataException("The credential description was too large.");
            }

            // Git writes LF, but a CRLF writer should not push a stray CR into a value.
            if (next != '\r')
            {
                line.Append((char) next);
            }
        }
    }

    /// <summary>Derives the repository a credential request is for.</summary>
    /// <param name="request">The fields Git supplied.</param>
    /// <param name="repository">The repository when one was identified.</param>
    /// <param name="error">Why no repository could be derived.</param>
    /// <returns>
    /// Whether the request is served, addressed here but unusable, or for something
    /// else entirely.
    /// </returns>
    /// <remarks>
    /// A missing path means <c>credential.useHttpPath</c> is not enabled, which would
    /// otherwise let one repository's token satisfy a request for another.
    /// </remarks>
    public static GitCredentialResolution TryResolveRepository(
        GitCredentialRequest request,
        out RepositoryName? repository,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        repository = null;

        if (!string.Equals(request.Protocol, "https", StringComparison.Ordinal))
        {
            error = "Only https credentials are served.";
            return GitCredentialResolution.NotServed;
        }

        if (!string.Equals(request.Host, GitHubHost.GitHubComName, StringComparison.Ordinal))
        {
            error = "Only github.com credentials are served.";
            return GitCredentialResolution.NotServed;
        }

        if (string.IsNullOrEmpty(request.Path))
        {
            error = "The credential request carries no repository path. Set "
                + "credential.https://github.com.useHttpPath=true so Git includes one.";

            return GitCredentialResolution.Unusable;
        }

        // Git supplies the clone path, which carries the ".git" suffix that GitHub
        // repository names never contain.
        var path = request.Path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? request.Path[..^4]
            : request.Path;

        if (RepositoryName.TryParse(path, out repository, out var parseError))
        {
            error = null;
            return GitCredentialResolution.Served;
        }

        error = $"The credential request names an invalid repository: {parseError}.";
        return GitCredentialResolution.Unusable;
    }

    /// <summary>Writes the credential Git expects, terminated by a blank line.</summary>
    /// <param name="output">The stream Git is reading.</param>
    /// <param name="token">The installation token to supply as the password.</param>
    /// <exception cref="InvalidDataException">
    /// The token holds a character that would end the value early.
    /// </exception>
    /// <remarks>
    /// The format is line-based, so a newline inside the token would append fields
    /// of an attacker's choosing to the credential Git reads. The token stays
    /// opaque otherwise: no prefix, length, or alphabet is assumed.
    /// </remarks>
    public static void WriteCredential(TextWriter output, string token)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(token);

        if (token.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new InvalidDataException("The token holds a character that cannot appear in a credential.");
        }

        output.Write($"username={TokenUsername}\n");
        output.Write($"password={token}\n");
        output.Write('\n');
        output.Flush();
    }
}
