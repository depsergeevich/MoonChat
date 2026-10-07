using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MoonChat.Client.Models;

namespace MoonChat.Client.Services;

/// <summary>
/// Thin REST client for the MoonChat API. It attaches the bearer token and
/// turns non-2xx replies into <see cref="ApiException"/> carrying the server's
/// error code, so the UI can react to specific failures.
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SessionStore _session;

    public ApiClient(HttpClient http, SessionStore session)
    {
        _http = http;
        _session = session;
    }

    public string BaseAddress => _http.BaseAddress?.ToString().TrimEnd('/') ?? "";



    public async Task<AuthResponse> RegisterAsync(string username, string displayName, string password, CancellationToken ct = default)
    {
        var result = await PostAsync<AuthResponse>("/api/auth/register", new
        {
            username,
            displayName,
            password,
        }, ct);
        await _session.SaveAsync(result.Token, result.User);
        return result;
    }

    public async Task<AuthResponse> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var result = await PostAsync<AuthResponse>("/api/auth/login", new { username, password }, ct);
        await _session.SaveAsync(result.Token, result.User);
        return result;
    }

    /// <summary>
    /// Revokes this device's token server-side, then drops it locally.
    ///
    /// The local clear happens even if the call fails: the user asked to sign
    /// out, and a server that cannot be reached is not a reason to stay signed
    /// in on this machine.
    /// </summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await SendAsync(HttpMethod.Post, "/api/auth/logout", null, ct);
        }
        catch (ApiException)
        {
            // Already invalid, or the server is gone. Either way, sign out here.
        }
        finally
        {
            _session.Clear();
        }
    }

    /// <summary>Revokes every session for this account, on every device.</summary>
    public async Task LogoutEverywhereAsync(CancellationToken ct = default)
    {
        await SendAsync(HttpMethod.Post, "/api/auth/logout-all", null, ct);
        _session.Clear();
    }

    public async Task<User> UpdateDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/me")
        {
            Content = JsonContent.Create(new { displayName }, options: JsonOptions),
        };
        var user = await SendAsync<User>(request, ct);

        // Keep the cached profile in step so the sidebar updates without a restart.
        await _session.SaveAsync(_session.Token, user);
        return user;
    }

    /// <summary>
    /// Changes the password, which signs every other device out.
    ///
    /// The server may answer with a freshly issued token because the old one is
    /// invalidated by the change; when it does, the session is replaced so the
    /// user is not bounced back to the sign-in screen they just used. A 204
    /// answer simply leaves the existing token alone.
    /// </summary>
    public async Task<AuthResponse?> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var result = await PostAsync<AuthResponse>("/api/users/me/password", new { currentPassword, newPassword }, ct);
        if (result is not null && !string.IsNullOrEmpty(result.Token) && result.User is not null)
        {
            await _session.SaveAsync(result.Token, result.User);
        }
        return result;
    }

    public async Task DeleteAccountAsync(string password, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/users/me")
        {
            Content = JsonContent.Create(new { password }, options: JsonOptions),
        };
        await SendAsync(request, ct);
        _session.Clear();
    }

    public async Task<User> DeleteAvatarAsync(CancellationToken ct = default)
    {
        var user = await SendAsync<User>(new HttpRequestMessage(HttpMethod.Delete, "/api/users/me/avatar"), ct);
        await _session.SaveAsync(_session.Token, user);
        return user;
    }

    public async Task<User> GetMeAsync(CancellationToken ct = default)
        => await GetAsync<User>("/api/auth/me", ct);

    public async Task<List<Contact>> GetContactsAsync(CancellationToken ct = default)
        => await GetAsync<List<Contact>>("/api/contacts", ct);

    public async Task<Contact> AddContactAsync(string username, CancellationToken ct = default)
        => await PostAsync<Contact>("/api/contacts", new { username }, ct);

    public Task RemoveContactAsync(long userId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"/api/contacts/{userId}", null, ct);

    public async Task<User> UploadAvatarAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var part = new StreamContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me/avatar") { Content = form };
        return await SendAsync<User>(request, ct);
    }

    public async Task<List<Conversation>> GetConversationsAsync(CancellationToken ct = default)
        => await GetAsync<List<Conversation>>("/api/conversations", ct);

    public async Task<OpenConversationResponse> OpenConversationAsync(long contactId, CancellationToken ct = default)
        => await PostAsync<OpenConversationResponse>("/api/conversations", new { contactId }, ct);

    /// <summary>
    /// Fetches one page of history. Passing only <paramref name="before"/>
    /// returns the newest page (the common "open a conversation" case);
    /// <paramref name="after"/> resyncs everything newer than a known id.
    /// </summary>
    public async Task<MessagePage> GetMessagesAsync(long conversationId, long? before = null, long? after = null, int limit = 50, CancellationToken ct = default)
    {
        var cursor = after is not null
            ? $"after={after.Value}"
            : $"before={before ?? long.MaxValue}";

        return await GetAsync<MessagePage>($"/api/conversations/{conversationId}/messages?{cursor}&limit={limit}", ct);
    }

    public async Task<ChatMessage> SendMessageAsync(long conversationId, string body, CancellationToken ct = default)
        => await PostAsync<ChatMessage>($"/api/conversations/{conversationId}/messages", new { body }, ct);

    public Task MarkReadAsync(long conversationId, long messageId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, $"/api/conversations/{conversationId}/read", new { messageId }, ct);

    /// <summary>
    /// Fetches the raw bytes of an avatar. The client needs the image data
    /// itself because a plain image request cannot carry the bearer token.
    /// </summary>
    public async Task<byte[]> DownloadAvatarAsync(string avatarUrl, CancellationToken ct = default)
    {
        var uri = ResolveAvatarUrl(avatarUrl);
        using var response = await _http.GetAsync(uri, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Builds an absolute URL for an avatar path. Only useful for diagnostics;
    /// rendering goes through <see cref="DownloadAvatarAsync"/>.
    /// </summary>
    public string ResolveAvatarUrl(string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return "";
        }

        if (avatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return avatarUrl;
        }

        return $"{BaseAddress}{avatarUrl.TrimStart('/')}";
    }

    // --- plumbing ---

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        return await SendAsync<T>(request, ct);
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        await SendAsync(request, ct);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default!;
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        if (result is null)
        {
            throw new ApiException("empty_response", "The server returned an empty response.");
        }
        return result;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiError? error = null;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ApiErrorBody>(JsonOptions, ct);
            error = body?.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Fall through to the generic message below.
        }

        var code = error?.Code ?? "http_" + (int)response.StatusCode;

        if (IsSessionExpiry(code))
        {
            // The token is no longer good for anything. Ending the session here
            // is what stops the UI limping along with a dead credential and
            // showing "Could not reach the server" for every request that
            // follows. Deliberately keyed on the error code and not the status:
            // a wrong password on /api/auth/login and a wrong current password
            // on the change-password call are also 401, and neither should
            // throw the user out.
            _session.Expire(
                code switch
                {
                    "session_expired" => "Your session expired. Please sign in again.",
                    _ => "You were signed out. Please sign in again.",
                });
        }

        throw new ApiException(
            code,
            error?.Message ?? $"Request failed with status {(int)response.StatusCode}.");
    }

    /// <summary>
    /// Error codes that mean "this token is dead", as opposed to "these
    /// credentials are wrong".
    /// </summary>
    private static bool IsSessionExpiry(string code) =>
        code is "unauthorized" or "session_expired" or "invalid_token";
}

/// <summary>An API failure with the server's machine-readable code.</summary>
public sealed class ApiException : Exception
{
    public ApiException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    /// <summary>
    /// True when the token itself was rejected, so the session must end. Wrong
    /// passwords are <c>invalid_credentials</c> and deliberately excluded.
    /// </summary>
    public bool IsAuthFailure => Code is "unauthorized" or "session_expired" or "invalid_token";

    /// <summary>True when the server is throttling this caller.</summary>
    public bool IsRateLimited => Code == "rate_limited";
}