using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;


namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Where the broker listens, and what the socket needs once it does.
/// </summary>
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

    /// <param name="app">The application whose socket is being narrowed.</param>
    extension(WebApplication app)
    {
        /// <summary>Gives the socket its configured mode.</summary>
        /// <param name="socket">The configured socket, or <see langword="null"/> for none.</param>
        /// <exception cref="UnixSocketModeException">The mode could not be applied.</exception>
        /// <remarks>
        /// Binding creates the socket file, so this belongs after the host has
        /// started rather than alongside <see cref="UseBrokerListeners"/>.
        /// </remarks>
        public void NarrowUnixSocket(UnixSocketOptions? socket)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (socket is null)
            {
                return;
            }

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

            BrokerHostLog.UnixSocketReady(app.Logger, socket.Path, socketMode);
        }
    }
}
