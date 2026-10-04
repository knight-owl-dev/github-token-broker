using KnightOwl.GitHubTokenBroker.Infrastructure.Configuration;
using KnightOwl.GitHubTokenBroker.Infrastructure.Diagnostics;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;
using KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Service;

// CA1031: a startup phase answers with a status rather than letting anything
// escape to the runtime, which is the whole reason these return one.
#pragma warning disable CA1031

/// <summary>
/// Brings the configured listeners into service, in the order each needs.
/// </summary>
/// <remarks>
/// A transport says what went wrong and this decides which status that earns,
/// so neither has to know the other.
/// </remarks>
internal static class BrokerHost
{
    /// <param name="app">The application being brought into service.</param>
    extension(WebApplication app)
    {
        /// <summary>Proves the App private key loads before anything is served.</summary>
        /// <returns>
        /// The status to exit with, or <see langword="null"/> when the key loads. A
        /// failure is reported before returning.
        /// </returns>
        /// <remarks>
        /// Without this a broker that cannot mint still answers /health and
        /// /v1/check, which reads as healthy. The per-mint read stays, and is what
        /// picks up a replacement key.
        /// </remarks>
        public async Task<int?> VerifyPrivateKeyAsync()
        {
            ArgumentNullException.ThrowIfNull(app);

            try
            {
                using var privateKey = app.Services.GetRequiredService<IPrivateKeySource>().Load();
                BrokerHostLog.PrivateKeyLoaded(app.Logger, privateKey.KeyId);
                return null;
            }
            catch (ConfigurationException exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "private key error", exception);
                return BrokerExitCode.PrivateKey;
            }
            catch (Exception exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "internal error", exception);
                return BrokerExitCode.Internal;
            }
        }

        /// <summary>Starts every configured listener and leaves it ready to serve.</summary>
        /// <param name="listen">The configured listener.</param>
        /// <returns>
        /// The status to exit with, or <see langword="null"/> once serving. A
        /// failure is reported before returning.
        /// </returns>
        /// <remarks>
        /// Either every listener is serving or none is, so anything failing after
        /// a bind stops the host on its way out.
        /// </remarks>
        public async Task<int?> StartServingAsync(ListenOptions listen)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(listen);

            UnixSocketPathState pathState;
            try
            {
                pathState = UnixSocketPreparation.Prepare(listen.UnixSocket.Path);
            }
            catch (ConfigurationException exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "configuration error", exception);
                return BrokerExitCode.Configuration;
            }
            catch (Exception exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "internal error", exception);
                return BrokerExitCode.Internal;
            }

            // Before binding, so the removal is on record even when the bind fails.
            if (pathState is UnixSocketPathState.Reclaimed)
            {
                BrokerHostLog.UnixSocketReclaimed(app.Logger, listen.UnixSocket.Path);
            }

            try
            {
                // Binding creates the socket file, and its mode then comes from the
                // umask until the narrowing below.
                using (RestrictedUmask.Apply())
                {
                    await app.StartAsync();
                }
            }
            catch (IOException exception)
            {
                // Kestrel binds when the host starts rather than when it is built,
                // so a path UnixSocketPreparation could not rule out arrives here.
                await DiagnosticReport.WriteAsync(Console.Error, "listener error", exception);
                return BrokerExitCode.Configuration;
            }

            try
            {
                var socketMode = BrokerListeners.NarrowUnixSocket(listen.UnixSocket);
                BrokerHostLog.UnixSocketReady(app.Logger, listen.UnixSocket.Path, socketMode);
            }
            catch (UnixSocketModeException exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "listener error", exception);
                await app.StopAsync();
                return BrokerExitCode.SocketMode;
            }
            catch (Exception exception)
            {
                await DiagnosticReport.WriteAsync(Console.Error, "internal error", exception);
                await app.StopAsync();
                return BrokerExitCode.Internal;
            }

            return null;
        }
    }
}
