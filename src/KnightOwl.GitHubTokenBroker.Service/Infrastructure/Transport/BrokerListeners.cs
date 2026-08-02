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
        /// <summary>Binds every configured listener.</summary>
        /// <param name="listen">The configured listeners.</param>
        /// <exception cref="ConfigurationException">
        /// A Unix socket path is not free to bind. Kestrel raises the rest when the
        /// application is built.
        /// </exception>
        public void UseBrokerListeners(ListenOptions listen)
        {
            ArgumentNullException.ThrowIfNull(webHost);
            ArgumentNullException.ThrowIfNull(listen);

            if (listen.UnixSocket is { } unixSocket)
            {
                UnixSocketPreparation.Prepare(unixSocket.Path);
            }

            webHost.ConfigureKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = BrokerProtocol.MaxRequestBytes;
                    options.AddServerHeader = false;

                    if (listen.UnixSocket is { } socket)
                    {
                        options.ListenUnixSocket(socket.Path);
                    }

                    if (listen.Tcp is { } tcp)
                    {
                        options.Listen(
                            tcp.Address,
                            tcp.Port,
                            listener => listener.Use(next => async connection =>
                                {
                                    // Marks the connection so the credential gate can require a secret
                                    // here without imposing one on the Unix socket.
                                    connection.Features.Set(new TcpTransportMarker());
                                    await next(connection);
                                }
                            )
                        );
                    }
                }
            );
        }
    }

    /// <param name="app">The application whose socket is being narrowed.</param>
    extension(WebApplication app)
    {
        /// <summary>
        /// Applies the configured mode to the socket once it exists, and stops the
        /// broker if it cannot.
        /// </summary>
        /// <param name="socket">The configured socket, or <see langword="null"/> for none.</param>
        /// <remarks>
        /// The socket file exists only once the listener does, so the mode cannot be
        /// applied alongside <see cref="UseBrokerListeners"/>. Failing stops the broker:
        /// this transport cannot serve on a socket whose permissions are unknown.
        /// </remarks>
        public void ApplyUnixSocketModeOnStart(UnixSocketOptions? socket)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (socket is null)
            {
                return;
            }

            var socketMode = UnixSocketPreparation.Format(socket.Mode);
            app.Lifetime.ApplicationStarted.Register(() =>
                {
                    try
                    {
                        if (!OperatingSystem.IsWindows())
                        {
                            File.SetUnixFileMode(socket.Path, socket.Mode);
                        }

                        BrokerHostLog.UnixSocketReady(app.Logger, socket.Path, socketMode);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        BrokerHostLog.UnixSocketModeFailed(
                            app.Logger,
                            socket.Path,
                            socketMode,
                            exception
                        );

                        app.Lifetime.StopApplication();
                    }
                }
            );
        }
    }
}
