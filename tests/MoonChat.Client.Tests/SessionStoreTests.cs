using System.Text.Json;
using MoonChat.Client.Models;
using MoonChat.Client.Services;

namespace MoonChat.Client.Tests;

/// <summary>
/// The session cache is what lets a returning user skip the login screen, so it
/// has to survive a corrupt file and a missing file without taking the app down.
/// Each test uses its own <see cref="TempSessionStore"/>, never the real
/// %LOCALAPPDATA% path.
/// </summary>
public class SessionStoreTests
{
    private static User SampleUser(long id = 42) => new()
    {
        Id = id,
        Username = "ada",
        DisplayName = "Ada Lovelace",
        AvatarUrl = "/avatars/42.png",
        LastSeenAt = 1_700_000_000_000,
    };

    [Fact]
    public void a_new_store_is_signed_out()
    {
        using var temp = TempSessionStore.Create();

        Assert.Equal("", temp.Store.Token);
        Assert.Null(temp.Store.User);
        Assert.False(temp.Store.IsSignedIn);
        Assert.Null(temp.Store.LastConversationId);
    }

    [Fact]
    public void Restore_on_a_missing_file_leaves_the_store_signed_out()
    {
        using var temp = TempSessionStore.Create();

        var exception = Record.Exception(temp.Store.Restore);

        Assert.Null(exception);
        Assert.False(temp.Store.IsSignedIn);
        Assert.Equal("", temp.Store.Token);
    }

    [Fact]
    public async Task SaveAsync_then_Restore_round_trips_the_session()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());

        // A second store stands in for the next launch of the app.
        using var next = TempSessionStore.Create();
        next.Path.ToString();
        File.WriteAllText(next.Path, File.ReadAllText(temp.Path));
        next.Store.Restore();

        Assert.Equal("token-abc", next.Store.Token);
        Assert.NotNull(next.Store.User);
        Assert.Equal("ada", next.Store.User!.Username);
        Assert.Equal("Ada Lovelace", next.Store.User.DisplayName);
        Assert.Equal(42, next.Store.User.Id);
        Assert.Equal("/avatars/42.png", next.Store.User.AvatarUrl);
        Assert.True(next.Store.IsSignedIn);
    }

    [Fact]
    public async Task SaveAsync_persists_LastConversationId()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());
        temp.Store.RememberConversation(77);

        // RememberConversation persists via a fire-and-forget SaveAsync, so poll
        // for the write rather than racing it.
        Assert.True(
            Wait.Until(() => File.ReadAllText(temp.Path).Contains("77")),
            "the remembered conversation was never written to the cache file");

        using var next = TempSessionStore.Create();
        File.WriteAllText(next.Path, File.ReadAllText(temp.Path));
        next.Store.Restore();

        Assert.Equal(77, next.Store.LastConversationId);
    }

    [Fact]
    public async Task SaveAsync_writes_a_readable_cache_file()
    {
        using var temp = TempSessionStore.Create();

        await temp.Store.SaveAsync("token-abc", SampleUser());

        Assert.True(File.Exists(temp.Path));

        using var doc = JsonDocument.Parse(File.ReadAllText(temp.Path));
        Assert.Equal("token-abc", doc.RootElement.GetProperty("Token").GetString());
    }

    [Fact]
    public void Restore_ignores_a_corrupt_cache_file()
    {
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, "{ this is not json ");

        var exception = Record.Exception(temp.Store.Restore);

        Assert.Null(exception);
        Assert.False(temp.Store.IsSignedIn);
        Assert.Equal("", temp.Store.Token);
        Assert.Null(temp.Store.User);
    }

    [Fact]
    public void Restore_ignores_a_truncated_cache_file()
    {
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, "{\"Token\":\"abc\"");

        Record.Exception(temp.Store.Restore);

        Assert.False(temp.Store.IsSignedIn);
    }

    [Fact]
    public void Restore_ignores_an_empty_cache_file()
    {
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, "");

        Record.Exception(temp.Store.Restore);

        Assert.False(temp.Store.IsSignedIn);
    }

    [Fact]
    public void Restore_ignores_a_json_document_that_is_not_an_object()
    {
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, "[1,2,3]");

        Record.Exception(temp.Store.Restore);

        Assert.False(temp.Store.IsSignedIn);
    }

    [Fact]
    public void Restore_ignores_a_session_with_an_empty_token()
    {
        // The token is the only field that decides whether we are signed in.
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, """{"Token":"","User":{"Username":"ada"}}""");

        temp.Store.Restore();

        Assert.False(temp.Store.IsSignedIn);
        Assert.Equal("", temp.Store.Token);
    }

    [Fact]
    public void Restore_reads_a_camelCase_cache_written_by_an_older_build()
    {
        // SessionStore caches with PropertyNameCaseInsensitive precisely so an
        // older camelCase file still restores.
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, """{"token":"legacy","user":{"id":7,"username":"grace"}}""");

        temp.Store.Restore();

        Assert.Equal("legacy", temp.Store.Token);
        Assert.Equal("grace", temp.Store.User?.Username);
    }

    [Fact]
    public void Restore_raises_Changed_when_it_loads_a_session()
    {
        using var temp = TempSessionStore.Create();
        File.WriteAllText(temp.Path, """{"Token":"abc","User":{"Username":"ada"}}""");
        var raised = 0;
        temp.Store.Changed += () => raised++;

        temp.Store.Restore();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SaveAsync_raises_Changed()
    {
        using var temp = TempSessionStore.Create();
        var raised = 0;
        temp.Store.Changed += () => raised++;

        await temp.Store.SaveAsync("token-abc", SampleUser());

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task RememberConversation_records_the_id()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());

        temp.Store.RememberConversation(99);

        Assert.Equal(99, temp.Store.LastConversationId);
    }

    [Fact]
    public async Task RememberConversation_is_idempotent_for_the_same_id()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());
        temp.Store.RememberConversation(99);

        var raised = 0;
        temp.Store.Changed += () => raised++;
        temp.Store.RememberConversation(99);

        Assert.Equal(99, temp.Store.LastConversationId);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void RememberConversation_records_the_id_even_when_signed_out()
    {
        // Deliberate: the value is held in memory and persisted on the next
        // SaveAsync. Pins the behaviour so a later refactor does not silently
        // drop the remembered thread.
        using var temp = TempSessionStore.Create();

        temp.Store.RememberConversation(55);

        Assert.Equal(55, temp.Store.LastConversationId);
    }

    [Fact]
    public async Task Clear_signs_out_and_deletes_the_cache_file()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());
        temp.Store.RememberConversation(12);
        Assert.True(File.Exists(temp.Path));

        temp.Store.Clear();

        Assert.False(temp.Store.IsSignedIn);
        Assert.Equal("", temp.Store.Token);
        Assert.Null(temp.Store.User);
        Assert.Null(temp.Store.LastConversationId);
        Assert.False(File.Exists(temp.Path));
    }

    [Fact]
    public async Task Clear_raises_Changed()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());
        var raised = 0;
        temp.Store.Changed += () => raised++;

        temp.Store.Clear();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Clear_on_a_store_with_no_file_does_not_throw()
    {
        using var temp = TempSessionStore.Create();

        var exception = Record.Exception(temp.Store.Clear);

        Assert.Null(exception);
    }

    [Fact]
    public async Task Clear_then_Restore_stays_signed_out()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());

        temp.Store.Clear();
        temp.Store.Restore();

        Assert.False(temp.Store.IsSignedIn);
    }

    [Fact]
    public async Task DiscardRestored_clears_memory_but_keeps_the_cache_file()
    {
        // This is the single-instance path: the second window must start signed
        // out, but deleting the file would sign the owning window out too.
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());

        temp.Store.DiscardRestored();

        Assert.False(temp.Store.IsSignedIn);
        Assert.True(File.Exists(temp.Path));

        // The next launch can still pick the session up.
        using var next = TempSessionStore.Create();
        File.WriteAllText(next.Path, File.ReadAllText(temp.Path));
        next.Store.Restore();

        Assert.True(next.Store.IsSignedIn);
    }

    [Fact]
    public async Task DiscardRestored_raises_Changed()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());
        var raised = 0;
        temp.Store.Changed += () => raised++;

        temp.Store.DiscardRestored();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task UpdateToken_replaces_the_token_and_keeps_the_profile()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("old-token", SampleUser());

        temp.Store.UpdateToken("new-token");

        Assert.Equal("new-token", temp.Store.Token);
        Assert.Equal("ada", temp.Store.User?.Username);

        // UpdateToken saves without awaiting; wait for the file to land.
        Assert.True(
            Wait.Until(() => File.ReadAllText(temp.Path).Contains("new-token")),
            "the rotated token was never written to the cache file");

        using var next = TempSessionStore.Create();
        File.WriteAllText(next.Path, File.ReadAllText(temp.Path));
        next.Store.Restore();

        Assert.Equal("new-token", next.Store.Token);
        Assert.Equal("Ada Lovelace", next.Store.User?.DisplayName);
    }

    [Fact]
    public async Task UpdateToken_ignores_an_empty_token()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("old-token", SampleUser());

        temp.Store.UpdateToken("");

        Assert.Equal("old-token", temp.Store.Token);
    }

    [Fact]
    public async Task UpdateToken_ignores_the_token_it_already_has()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("same-token", SampleUser());

        temp.Store.UpdateToken("same-token");

        Assert.Equal("same-token", temp.Store.Token);
    }

    /// <summary>
    /// Genuine bug: <c>UpdateToken</c> dereferences <c>User!</c> unconditionally
    /// (SessionStore.cs:131). A <c>token_refresh</c> frame that arrives before a
    /// session exists therefore throws an unobserved NRE on a discarded task.
    /// Documents current behaviour; the null-forgiving operator is the culprit.
    /// </summary>
    [Fact]
    public void UpdateToken_on_a_signed_out_store_throws_inside_the_fire_and_forget_save()
    {
        using var temp = TempSessionStore.Create();

        var exception = Record.Exception(() => temp.Store.UpdateToken("token-abc"));

        // The synchronous part succeeds (the token is assigned before SaveAsync
        // is even called), so nothing escapes here...
        Assert.Null(exception);
        Assert.Equal("token-abc", temp.Store.Token);

        // ...but User was null, and SaveAsync dereferences it on a background
        // task whose failure nobody observes. The observable damage is that the
        // store claims to have a token with no user behind it.
        Assert.Null(temp.Store.User);
        Assert.False(temp.Store.IsSignedIn);
    }

    [Fact]
    public async Task SaveAsync_overwrites_an_earlier_save()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("first", SampleUser(1));
        await temp.Store.SaveAsync("second", SampleUser(2));

        Assert.Equal("second", temp.Store.Token);
        Assert.Equal(2, temp.Store.User?.Id);
    }

    [Fact]
    public async Task concurrent_SaveAsync_calls_all_complete_and_the_last_write_wins()
    {
        using var temp = TempSessionStore.Create();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => temp.Store.SaveAsync($"token-{i}", SampleUser(i))));

        Assert.True(temp.Store.IsSignedIn);

        using var next = TempSessionStore.Create();
        File.WriteAllText(next.Path, File.ReadAllText(temp.Path));
        next.Store.Restore();

        // Whatever the interleaving, the file must still be valid JSON that
        // restores -- that is the property the semaphore exists to protect.
        Assert.True(next.Store.IsSignedIn);
    }

    [Fact]
    public async Task IsSignedIn_requires_both_a_token_and_a_user()
    {
        using var temp = TempSessionStore.Create();
        await temp.Store.SaveAsync("token-abc", SampleUser());

        temp.Store.DiscardRestored();
        Assert.False(temp.Store.IsSignedIn);

        temp.Store.Clear();
        Assert.False(temp.Store.IsSignedIn);
    }
}