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

        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            buff.AppendLine($"{Indent}{inner.Message}");
        }

        await error.WriteAsync(buff.ToString());
    }
}
