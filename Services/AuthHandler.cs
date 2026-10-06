namespace MoonChat.Client.Services;

/// <summary>
/// Attaches the bearer token to every outbound API request. Keeping this in a
/// handler means no call site can forget it, and the token can rotate (see the
/// <c>refresh_token</c> websocket frame) without changing the REST surface.
/// </summary>
public sealed class AuthHandler : DelegatingHandler
{
    private readonly SessionStore _session;

    public AuthHandler(SessionStore session)
    {
        _session = session;
        InnerHandler = new HttpClientHandler();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = _session.Token;
        if (!string.IsNullOrEmpty(token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, ct);
    }
}