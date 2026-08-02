using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;

/// <summary>
/// Talks to the broker over HTTP, whether that HTTP rides a Unix socket or TCP.
/// </summary>
/// <remarks>
/// The transport difference lives entirely in the supplied <see cref="HttpClient"/>,
/// so request handling, error classification, and everything above are identical on
/// both. See <see cref="BrokerHttpClientFactory"/>.
/// </remarks>
public sealed class HttpBrokerClient : IBrokerClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _clientCredential;

    /// <summary>Creates the client.</summary>
    /// <param name="httpClient">Client already bound to the broker endpoint.</param>
    /// <param name="clientCredential">
    /// Credential for the TCP transport, or <see langword="null"/> on a Unix socket.
    /// </param>
    public HttpBrokerClient(HttpClient httpClient, string? clientCredential)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _clientCredential = clientCredential;
    }

    /// <inheritdoc/>
    public Task<BrokerTokenResponse> RequestTokenAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    )
        => PostAsync(
            BrokerV1Routes.TokenPath,
            repository,
            BrokerV1JsonContext.Default.BrokerTokenResponse,
            cancellationToken
        );

    /// <inheritdoc/>
    public Task<BrokerCheckResponse> CheckAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    )
        => PostAsync(
            BrokerV1Routes.CheckPath,
            repository,
            BrokerV1JsonContext.Default.BrokerCheckResponse,
            cancellationToken
        );

    /// <inheritdoc/>
    public void Dispose()
        => _httpClient.Dispose();

    private async Task<TResponse> PostAsync<TResponse>(
        string path,
        RepositoryName repository,
        JsonTypeInfo<TResponse> typeInfo,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(repository);

        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(
                    new BrokerRepositoryRequest
                    {
                        Host = GitHubHost.GitHubComName,
                        Repository = repository.FullName,
                    },
                    BrokerV1JsonContext.Default.BrokerRepositoryRequest
                ),
                Encoding.UTF8,
                MediaTypeNames.Application.Json
            ),
        };

        if (_clientCredential is not null)
        {
            request.Headers.Add(BrokerProtocol.ClientCredentialHeader, _clientCredential);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrokerClientException(
                BrokerClientFailure.Unavailable,
                "The broker did not answer in time.",
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            throw new BrokerClientException(
                BrokerClientFailure.Unavailable,
                "The broker could not be reached.",
                exception
            );
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new BrokerClientException(
                    BrokerClientFailure.Refused,
                    $"The broker does not serve {repository.FullName}."
                );
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new BrokerClientException(
                    BrokerClientFailure.Unauthenticated,
                    "The broker rejected the client credential."
                );
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new BrokerClientException(
                    BrokerClientFailure.Misconfigured,
                    $"The broker cannot mint for {repository.FullName}: it is allowlisted, but the GitHub App installation does not grant it. Check the installation's repository access."
                );
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BrokerClientException(
                    BrokerClientFailure.Failed,
                    $"The broker returned status {(int) response.StatusCode}. See the broker log."
                );
            }

            TResponse? body;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync(typeInfo, cancellationToken);
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new BrokerClientException(
                    BrokerClientFailure.Failed,
                    "The broker response could not be parsed.",
                    exception
                );
            }

            return body
                ?? throw new BrokerClientException(
                    BrokerClientFailure.Failed,
                    "The broker returned an empty response."
                );
        }
    }
}
