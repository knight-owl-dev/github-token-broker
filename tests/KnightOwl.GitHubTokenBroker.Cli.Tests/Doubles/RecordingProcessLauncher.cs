using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;

/// <summary>Records what would have been launched instead of starting a process.</summary>
internal sealed class RecordingProcessLauncher : IProcessLauncher
{
    private readonly int _exitCode;

    public RecordingProcessLauncher(int exitCode = 0)
        => _exitCode = exitCode;

    public bool WasInvoked { get; private set; }

    public string? ExecutablePath { get; private set; }

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public IReadOnlyDictionary<string, string?> Environment { get; private set; } =
        new Dictionary<string, string?>();

    public int Run(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environmentOverrides
    )
    {
        this.WasInvoked = true;
        this.ExecutablePath = executablePath;
        this.Arguments = arguments;
        this.Environment = environmentOverrides;
        return _exitCode;
    }
}
