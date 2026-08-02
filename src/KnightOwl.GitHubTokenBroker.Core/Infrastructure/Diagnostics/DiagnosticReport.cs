using System.Text;


namespace KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;

/// <summary>
/// Writes a failure to a stream a person reads.
/// </summary>
/// <remarks>
/// A wrapped exception keeps its own wording instead of having it spliced into
/// ours, so the framework's voice and this one never share a sentence and detail
/// such as a JSON line and byte position still reaches the reader.
/// </remarks>
public static class DiagnosticReport
{
    private const string Indent = "  ";

    /// <summary>Writes an exception and everything it wraps.</summary>
    /// <param name="error">Where diagnostics are written.</param>
    /// <param name="label">Prefix for the first line, or <see langword="null"/> for none.</param>
    /// <param name="exception">The failure to report.</param>
    public static async Task WriteAsync(TextWriter error, string? label, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(exception);

        var buff = new StringBuilder();
        buff.AppendLine(label is null ? exception.Message : $"{label}: {exception.Message}");

        foreach (var inner in Wrapped(exception))
        {
            buff.AppendLine($"{Indent}{inner}");
        }

        await error.WriteAsync(buff.ToString());
    }

    /// <summary>Renders an exception and everything it wraps on one line.</summary>
    /// <param name="exception">The failure to describe.</param>
    /// <returns>The chain, outermost first.</returns>
    /// <remarks>
    /// For a log, where the indented form above would span records. Joined with a
    /// space, since each message is its own sentence.
    /// </remarks>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return string.Join(' ', new[] { exception.Message }.Concat(Wrapped(exception)));
    }

    private static IEnumerable<string> Wrapped(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            yield return inner.Message;
        }
    }
}
