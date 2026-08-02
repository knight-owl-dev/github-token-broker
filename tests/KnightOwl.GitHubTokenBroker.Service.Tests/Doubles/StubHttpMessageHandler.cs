namespace KnightOwl.GitHubTokenBroker.Service.Tests.Doubles;

/// <summary>
/// Answers HTTP requests from a supplied delegate and records what was sent, so the
/// GitHub adapter is tested without a network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
        => _respond = respond;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        this.Requests.Add(request);
        this.Bodies.Add(body);
        return _respond(request, body);
    }
}
