using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// What configured transport needs to bind, and to be usable once it has.
/// </summary>
/// <remarks>
/// Mechanics only. When each step runs, and what its failure costs the process,
/// belongs to the host that orders them.
/// </remarks>
internal static class BrokerListeners
{
    /// <param name="webHost">The host to add the listeners to.</param>
    extension(IWebHostBuilder webHost)
    {
        /// <summary>Binds the configured listener.</summary>
        /// <param name="listen">The configured listener.</param>
        /// <exception cref="ConfigurationException">
        /// The socket path is not free to bind.
        /// </exception>
        public void UseBrokerListeners(ListenOptions listen)
        {
            ArgumentNullException.ThrowIfNull(webHost);
            ArgumentNullException.ThrowIfNull(listen);

            UnixSocketPreparation.Prepare(listen.UnixSocket.Path);

            webHost.ConfigureKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = BrokerProtocol.MaxRequestBytes;
                    options.AddServerHeader = false;
                    options.ListenUnixSocket(listen.UnixSocket.Path);
                }
            );
        }
    }

    /// <summary>Gives a bound socket its configured mode.</summary>
    /// <param name="socket">The configured socket.</param>
    /// <returns>The mode applied, as the octal an operator wrote.</returns>
    /// <exception cref="UnixSocketModeException">The mode could not be applied.</exception>
    /// <remarks>
    /// Binding creates the socket file, so this only works once the host has
    /// started.
    /// </remarks>
    public static string NarrowUnixSocket(UnixSocketOptions socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var socketMode = UnixSocketPreparation.Format(socket.Mode);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(socket.Path, socket.Mode);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new UnixSocketModeException(
                $"The socket \"{socket.Path}\" could not be given mode {socketMode}.",
                exception
            );
        }

        return socketMode;
    }
}
