using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Domain.Access;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;
using KnightOwl.GitHubTokenBroker.Infrastructure.Transport;


namespace KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;

/// <summary>
/// Talks to the broker over HTTP carried on a Unix socket.
/// </summary>
/// <remarks>
/// The transport lives entirely in the supplied <see cref="HttpClient"/>; see
/// <see cref="BrokerHttpClientFactory"/>.
/// </remarks>
public sealed class HttpBrokerClient : IBrokerClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly BrokerEndpoint _endpoint;

    /// <summary>Creates the client over a caller-supplied transport.</summary>
    /// <param name="httpClient">Client bound to <paramref name="endpoint"/>.</param>
    /// <param name="endpoint">The endpoint, named when the broker cannot be reached.</param>
    /// <remarks>
    /// Nothing outside this assembly can hand the two arguments a transport that
    /// disagrees with the endpoint it reports; <see cref="Create"/> derives one
    /// from the other.
    /// </remarks>
    internal HttpBrokerClient(HttpClient httpClient, BrokerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);

        _httpClient = httpClient;
        _endpoint = endpoint;
    }

    /// <summary>Creates a client and the transport it talks over.</summary>
    /// <param name="endpoint">The broker endpoint to reach.</param>
    /// <returns>A client the caller owns and must dispose.</returns>
    public static HttpBrokerClient Create(BrokerEndpoint endpoint)
        => new(BrokerHttpClientFactory.Create(endpoint), endpoint);

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

        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        request.Content = new StringContent(
            JsonSerializer.Serialize(
                new BrokerRepositoryRequest
                {
                    Host = GitHubHost.GitHubComName,
                    Repository = repository.FullName,
                },
                BrokerV1JsonContext.Default.BrokerRepositoryRequest
            ),
            Encoding.UTF8,
            MediaTypeNames.Application.Json
        );

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BrokerClientException(
                BrokerClientFailure.Unavailable,
                $"The broker at {_endpoint} did not answer in time.",
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            // Named because the wrapped message reports the request URI, whose
            // authority is a placeholder on a Unix socket.
            throw new BrokerClientException(
                BrokerClientFailure.Unavailable,
                $"The broker at {_endpoint} could not be reached.",
                exception
            );
        }

        using (response)
        {
            var failure = response.StatusCode switch
            {
                HttpStatusCode.Forbidden => new BrokerClientException(
                    BrokerClientFailure.Refused,
                    $"The broker does not serve {repository.FullName}."
                ),
                HttpStatusCode.Conflict => new BrokerClientException(
                    BrokerClientFailure.Misconfigured,
                    $"The broker cannot mint for {repository.FullName}: it is allowlisted, "
                    + "but the GitHub App installation does not grant it. "
                    + "Check the installation's repository access."
                ),
                _ when !response.IsSuccessStatusCode => new BrokerClientException(
                    BrokerClientFailure.Failed,
                    $"The broker returned status {(int) response.StatusCode}. See the broker log."
                ),
                _ => null,
            };

            if (failure is not null)
            {
                throw failure;
            }

            TResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            }
            catch (JsonException exception)
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
