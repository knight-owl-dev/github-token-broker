using KnightOwl.GitHubTokenBroker.Cli.Application.Ports;
using KnightOwl.GitHubTokenBroker.Cli.Infrastructure.Broker;
using KnightOwl.GitHubTokenBroker.Domain.Repositories;
using KnightOwl.GitHubTokenBroker.Infrastructure.Contracts.V1;


namespace KnightOwl.GitHubTokenBroker.Cli.Tests.Doubles;

/// <summary>A broker that answers from a script, recording what was asked of it.</summary>
internal sealed class StubBrokerClient : IBrokerClient
{
    private readonly string? _token;
    private readonly BrokerClientFailure? _failure;

    private StubBrokerClient(string? token, BrokerClientFailure? failure)
    {
        _token = token;
        _failure = failure;
    }

    public List<string> TokenRequests { get; } = [];

    public List<string> CheckRequests { get; } = [];

    public static StubBrokerClient Returning(string token)
        => new(token, null);

    public static StubBrokerClient Failing(BrokerClientFailure failure)
        => new(null, failure);

    public Task<BrokerTokenResponse> RequestTokenAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    )
    {
        this.TokenRequests.Add(repository.FullName);

        return _failure is { } classified
            ? Task.FromException<BrokerTokenResponse>(new BrokerClientException(classified, $"stub failure: {classified}"))
            : Task.FromResult(
                new BrokerTokenResponse
                {
                    Token = _token!,
                    ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(100),
                }
            );
    }

    public Task<BrokerCheckResponse> CheckAsync(
        RepositoryName repository,
        CancellationToken cancellationToken
    )
    {
        this.CheckRequests.Add(repository.FullName);

        return _failure is { } classified
            ? Task.FromException<BrokerCheckResponse>(new BrokerClientException(classified, $"stub failure: {classified}"))
            : Task.FromResult(
                new BrokerCheckResponse
                {
                    Repository = repository.FullName,
                    Permissions = "contents:write",
                }
            );
    }
}
