namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Transport;

/// <summary>
/// Connection feature stamped on connections arriving over TCP.
/// </summary>
/// <remarks>
/// Both transports can run at once, and only the TCP one requires a client
/// credential. Kestrel surfaces connection features on the request, so marking the
/// listener is a precise way to tell them apart, rather than inferring it from an
/// absent remote port.
/// </remarks>
public sealed class TcpTransportMarker;
