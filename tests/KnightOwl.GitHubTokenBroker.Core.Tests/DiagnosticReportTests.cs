using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;


namespace KnightOwl.GitHubTokenBroker.Core.Tests;

public sealed class DiagnosticReportTests
{
    [Fact]
    public async Task LabelsTheFirstLine()
    {
        await using StringWriter error = new();

        await DiagnosticReport.WriteAsync(
            error,
            "github-token",
            new InvalidOperationException("The broker is unavailable.")
        );

        // Exact, because a report that ends in a blank line is what buffering it gets wrong.
        Assert.Equal($"github-token: The broker is unavailable.{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task WritesTheMessageAloneWhenThereIsNoLabel()
    {
        await using StringWriter error = new();

        await DiagnosticReport.WriteAsync(error, null, new InvalidOperationException("The command line is wrong."));

        Assert.Equal($"The command line is wrong.{Environment.NewLine}", error.ToString());
    }

    [Fact]
    public async Task IndentsEveryWrappedMessageOutward()
    {
        await using StringWriter error = new();
        FormatException innermost = new("Unexpected character at line 2, position 7.");
        IOException inner = new("The response could not be read.", innermost);

        await DiagnosticReport.WriteAsync(
            error,
            "github-token",
            new InvalidOperationException("The broker returned an unusable response.", inner)
        );

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "github-token: The broker returned an unusable response.",
                "  The response could not be read.",
                "  Unexpected character at line 2, position 7.",
                ""
            ),
            error.ToString()
        );
    }
}
