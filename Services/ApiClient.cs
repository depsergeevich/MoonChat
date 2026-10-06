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

    public Task LogoutAsync()
    {
        _session.Clear();
        return Task.CompletedTask;
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
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

        throw new ApiException(
            error?.Code ?? "http_" + (int)response.StatusCode,
            error?.Message ?? $"Request failed with status {(int)response.StatusCode}.");
    }
}

/// <summary>An API failure with the server's machine-readable code.</summary>
public sealed class ApiException : Exception
{
    public ApiException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    /// <summary>True when the session is no longer valid and the user must sign in again.</summary>
    public bool IsAuthFailure => Code is "unauthorized";
}