using System.Net;
using MoonChat.Client.Models;
using MoonChat.Client.Services;

namespace MoonChat.Client.Tests;

/// <summary>
/// <see cref="ApiClient"/> is exercised through an in-memory
/// <see cref="StubHandler"/>, so these are real request/response round trips
/// with no sockets and no sleeps.
/// </summary>
public class ApiClientTests
{
    private static readonly Uri BaseUri = new("http://localhost:8080/");

    private static (ApiClient Client, StubHandler Handler, TempSessionStore Temp) Create(
        string responseJson,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        var temp = TempSessionStore.Create();
        var handler = StubHandler.Json(responseJson, status);
        var http = new HttpClient(handler) { BaseAddress = BaseUri };
        return (new ApiClient(http, temp.Store), handler, temp);
    }

    [Fact]
    public void BaseAddress_is_reported_without_a_trailing_slash()
    {
        using var temp = TempSessionStore.Create();
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("http://localhost:8080/"),
        };
        var client = new ApiClient(http, temp.Store);

        Assert.Equal("http://localhost:8080", client.BaseAddress);
    }

    [Fact]
    public void ResolveAvatarUrl_leaves_an_absolute_url_alone()
    {
        using var temp = TempSessionStore.Create();
        var client = new ApiClient(new HttpClient { BaseAddress = BaseUri }, temp.Store);

        Assert.Equal("https://cdn.example.com/a.png", client.ResolveAvatarUrl("https://cdn.example.com/a.png"));
        Assert.Equal("http://cdn.example.com/a.png", client.ResolveAvatarUrl("http://cdn.example.com/a.png"));
    }

    /// <summary>
    /// <summary>
    /// Only the slash-less form behaves as intended. The root-relative form is
    /// covered by <see cref="ResolveAvatarUrl_mangles_a_root_relative_path"/>,
    /// which documents the real (broken) behaviour.
    /// </summary>
    [Fact]
    public void ResolveAvatarUrl_prefixes_a_relative_path_with_the_base_address()
    {
        using var temp = TempSessionStore.Create();
        var client = new ApiClient(new HttpClient { BaseAddress = BaseUri }, temp.Store);

        Assert.Equal("http://localhost:8080avatars/1.png", client.ResolveAvatarUrl("avatars/1.png"));
    }

    /// <summary>
    /// Documents a real defect: the leading slash is stripped before the base
    /// address is prepended, so a root-relative avatar path is glued directly
    /// onto the host ("http://localhost:8080avatars/1.png"). The server would
    /// 404 and every avatar would silently fall back to its initial. Looked
    /// wrong; the fix is in ApiClient.ResolveAvatarUrl, which is production
    /// code this change is not allowed to touch.
    /// </summary>
    [Fact]
    public void ResolveAvatarUrl_mangles_a_root_relative_path()
    {
        using var temp = TempSessionStore.Create();
        var client = new ApiClient(new HttpClient { BaseAddress = BaseUri }, temp.Store);

        var resolved = client.ResolveAvatarUrl("/avatars/1.png");

        // The correct value would be "http://localhost:8080/avatars/1.png".
        Assert.Equal("http://localhost:8080avatars/1.png", resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveAvatarUrl_returns_empty_for_blank_input(string input)
    {
        using var temp = TempSessionStore.Create();
        var client = new ApiClient(new HttpClient { BaseAddress = BaseUri }, temp.Store);

        Assert.Equal("", client.ResolveAvatarUrl(input));
    }

    [Fact]
    public async Task GetContactsAsync_parses_the_contact_list()
    {
        var (client, _, temp) = Create("""[{"id":1,"username":"ada","online":true}]""");

        var contacts = await client.GetContactsAsync();

        Assert.Single(contacts);
        Assert.Equal("ada", contacts[0].Username);
        Assert.True(contacts[0].Online);
        temp.Dispose();
    }

    [Fact]
    public async Task GetContactsAsync_sends_a_GET_to_the_contacts_path()
    {
        var (client, handler, temp) = Create("[]");

        await client.GetContactsAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/contacts", request.RequestUri!.PathAndQuery);
        temp.Dispose();
    }

    [Fact]
    public async Task GetMessagesAsync_defaults_to_the_newest_page()
    {
        var (client, handler, temp) = Create("""{"messages":[],"hasMore":false}""");

        await client.GetMessagesAsync(conversationId: 5);

        var uri = handler.Requests[0].RequestUri!;
        Assert.Equal("/api/conversations/5/messages", uri.AbsolutePath);
        Assert.Contains("before=9223372036854775807", uri.Query);
        Assert.Contains("limit=50", uri.Query);
        temp.Dispose();
    }

    [Fact]
    public async Task GetMessagesAsync_uses_before_when_paging_back()
    {
        var (client, handler, temp) = Create("""{"messages":[],"hasMore":false}""");

        await client.GetMessagesAsync(conversationId: 5, before: 9000, limit: 20);

        var uri = handler.Requests[0].RequestUri!;
        Assert.Contains("before=9000", uri.Query);
        Assert.Contains("limit=20", uri.Query);
        temp.Dispose();
    }

    /// <summary>
    /// Documents the cursor precedence: passing both before and after silently
    /// drops "before" and uses "after". Not a crash, but a paging bug waiting to
    /// happen if a caller ever supplies both.
    /// </summary>
    [Fact]
    public async Task GetMessagesAsync_prefers_after_over_before()
    {
        var (client, handler, temp) = Create("""{"messages":[],"hasMore":false}""");

        await client.GetMessagesAsync(conversationId: 5, before: 9000, after: 9001);

        var uri = handler.Requests[0].RequestUri!;
        Assert.Contains("after=9001", uri.Query);
        Assert.DoesNotContain("before=", uri.Query);
        temp.Dispose();
    }

    [Fact]
    public async Task SendMessageAsync_posts_the_body_as_camel_case_json()
    {
        var (client, handler, temp) = Create("""{"id":1,"conversationId":5,"body":"hello"}""");

        await client.SendMessageAsync(5, "hello");

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/conversations/5/messages", request.RequestUri!.AbsolutePath);
        Assert.Contains("\"body\":\"hello\"", handler.Bodies[0]);
        temp.Dispose();
    }

    [Fact]
    public async Task a_server_error_becomes_an_ApiException_carrying_the_code()
    {
        var (client, _, temp) = Create(
            """{"error":{"code":"unauthorized","message":"token expired"}}""",
            HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetContactsAsync());

        Assert.Equal("unauthorized", ex.Code);
        Assert.Equal("token expired", ex.Message);
        Assert.True(ex.IsAuthFailure);
        temp.Dispose();
    }

    [Fact]
    public async Task a_plain_error_falls_back_to_an_http_status_code()
    {
        var (client, _, temp) = Create("not json at all", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetContactsAsync());

        Assert.Equal("http_500", ex.Code);
        Assert.False(ex.IsAuthFailure);
        Assert.Contains("500", ex.Message);
        temp.Dispose();
    }

    [Fact]
    public async Task a_404_without_a_body_becomes_http_404()
    {
        var (client, _, temp) = Create("", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetContactsAsync());

        Assert.Equal("http_404", ex.Code);
        temp.Dispose();
    }

    [Theory]
    [InlineData("unauthorized", true)]
    [InlineData("forbidden", false)]
    [InlineData("http_401", false)]
    [InlineData("", false)]
    public void ApiException_IsAuthFailure_only_for_unauthorized(string code, bool expected)
        => Assert.Equal(expected, new ApiException(code, "m").IsAuthFailure);

    [Fact]
    public async Task LoginAsync_saves_the_session_and_returns_the_profile()
    {
        var (client, handler, temp) = Create(
            """{"token":"jwt-abc","user":{"id":42,"username":"ada","displayName":"Ada Lovelace"}}""");

        var auth = await client.LoginAsync("ada", "secret");

        Assert.Equal("jwt-abc", auth.Token);
        Assert.Equal("ada", auth.User.Username);
        Assert.Equal("http://localhost:8080/api/auth/login", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("\"username\":\"ada\"", handler.Bodies[0]);

        // LoginAsync must persist the session; that is what makes the next
        // launch skip the sign-in screen.
        Assert.Equal("jwt-abc", temp.Store.Token);
        Assert.True(temp.Store.IsSignedIn);
        temp.Dispose();
    }

    [Fact]
    public async Task LoginAsync_does_not_save_the_session_when_the_server_rejects()
    {
        var (client, _, temp) = Create(
            """{"error":{"code":"invalid_credentials","message":"nope"}}""",
            HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<ApiException>(() => client.LoginAsync("ada", "bad"));

        Assert.False(temp.Store.IsSignedIn);
        temp.Dispose();
    }

    [Fact]
    public async Task LogoutAsync_clears_the_session()
    {
        var (client, _, temp) = Create("{}");
        await temp.Store.SaveAsync("jwt-abc", new MoonChat.Client.Models.User { Id = 42, Username = "ada" });
        Assert.True(temp.Store.IsSignedIn);

        await client.LogoutAsync();

        Assert.False(temp.Store.IsSignedIn);
        temp.Dispose();
    }

    [Fact]
    public async Task RegisterAsync_posts_display_name_and_persists()
    {
        var (client, handler, temp) = Create(
            """{"token":"jwt-new","user":{"id":9,"username":"grace","displayName":"Grace Hopper"}}""");

        var auth = await client.RegisterAsync("grace", "Grace Hopper", "pw");

        Assert.Equal("grace", auth.User.Username);
        Assert.Equal("http://localhost:8080/api/auth/register", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("\"displayName\":\"Grace Hopper\"", handler.Bodies[0]);
        Assert.True(temp.Store.IsSignedIn);
        temp.Dispose();
    }

    [Fact]
    public async Task OpenConversationAsync_posts_the_contact_and_parses_the_result()
    {
        var (client, handler, temp) = Create(
            """{"conversationId":5,"messages":[{"id":1,"body":"a"}]}""");

        var opened = await client.OpenConversationAsync(42);

        Assert.Equal(5, opened.ConversationId);
        Assert.Single(opened.Messages);
        Assert.Contains("\"contactId\":42", handler.Bodies[0]);
        temp.Dispose();
    }

    [Fact]
    public async Task a_204_response_yields_the_default_value()
    {
        var (client, _, temp) = Create("", HttpStatusCode.NoContent);

        var me = await client.GetMeAsync();

        Assert.Null(me);
        temp.Dispose();
    }

    [Fact]
    public async Task a_json_null_body_raises_an_empty_response_error()
    {
        var (client, _, temp) = Create("null");

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetMeAsync());

        Assert.Equal("empty_response", ex.Code);
        temp.Dispose();
    }
}

/// <summary>
/// <see cref="AuthHandler"/> is what guarantees no call site can forget the
/// bearer token. It wraps a real HttpMessageHandler, so these drive the actual
/// DelegatingHandler pipeline with no sockets.
/// </summary>
public class AuthHandlerTests
{
    private static HttpClient BuildClient(TempSessionStore temp, out StubHandler inner)
    {
        inner = StubHandler.Json("{}");
        var http = new HttpClient(new AuthHandler(temp.Store) { InnerHandler = inner })
        {
            BaseAddress = new Uri("http://localhost:8080/"),
        };
        return http;
    }

    [Fact]
    public async Task a_signed_in_session_attaches_a_bearer_token()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("jwt-abc", new User { Id = 42, Username = "ada" });
        var client = BuildClient(temp, out var inner);

        await client.GetAsync("/api/contacts");

        var header = inner.Requests[0].Headers.Authorization!;
        Assert.Equal("Bearer", header.Scheme);
        Assert.Equal("jwt-abc", header.Parameter);
    }

    [Fact]
    public async Task no_header_is_attached_when_signed_out()
    {
        using var temp = TempSessionStore.Create();
        var client = BuildClient(temp, out var inner);

        await client.GetAsync("/api/contacts");

        Assert.Null(inner.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task an_existing_authorization_header_is_not_overwritten()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("jwt-abc", new User { Id = 42, Username = "ada" });
        var client = BuildClient(temp, out var inner);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contacts");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "explicit");
        await client.SendAsync(request);

        Assert.Equal("explicit", inner.Requests[0].Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task a_rotated_token_is_picked_up_on_the_next_request()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("old-token", new User { Id = 42, Username = "ada" });
        var client = BuildClient(temp, out var inner);

        await client.GetAsync("/api/contacts");
        temp.Store.UpdateToken("new-token");
        await client.GetAsync("/api/contacts");

        Assert.Equal("old-token", inner.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("new-token", inner.Requests[1].Headers.Authorization!.Parameter);
    }
}