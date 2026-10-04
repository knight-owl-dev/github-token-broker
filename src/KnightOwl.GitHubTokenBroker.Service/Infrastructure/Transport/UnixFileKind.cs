namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>What a path names, as <c>lstat</c> reports it: a link, not its target.</summary>
internal enum UnixFileKind
{
    /// <summary>The platform is unmapped, or <c>lstat</c> failed.</summary>
    Unknown,

    /// <summary>Nothing is at the path.</summary>
    Missing,

    /// <summary>A Unix domain socket.</summary>
    Socket,

    /// <summary>A symbolic link.</summary>
    SymbolicLink,

    /// <summary>A regular file, a directory, or any other kind.</summary>
    Other,
}
