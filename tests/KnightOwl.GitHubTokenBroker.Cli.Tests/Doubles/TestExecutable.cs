using System.Runtime.Versioning;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;

/// <summary>
/// Files standing in for the GitHub CLI, with and without the bit the locator
/// selects on.
/// </summary>
/// <remarks>
/// Shared so a candidate is executable everywhere it is meant to be found: every
/// fixture here was written by <c>File.WriteAllText</c> alone, which left them
/// all unusable and none of the tests saying so.
/// </remarks>
[UnsupportedOSPlatform("windows")]
internal static class TestExecutable
{
    private const UnixFileMode Executable =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Writes a script the locator will select.</summary>
    /// <param name="path">Where to write it. Its directory is created.</param>
    /// <returns>The path written.</returns>
    public static string Create(string path)
    {
        Write(path);
        File.SetUnixFileMode(path, Executable);
        return path;
    }

    /// <summary>Writes the same script without the execute bit.</summary>
    /// <param name="path">Where to write it. Its directory is created.</param>
    /// <returns>The path written.</returns>
    public static string CreateWithoutExecuteBit(string path)
    {
        Write(path);
        return path;
    }

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
    }
}
