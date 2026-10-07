using System.Text.Json;
using MoonChat.Client.Models;

namespace MoonChat.Client.Services;

/// <summary>
/// Holds the signed-in session. The token is cached in memory and mirrored to a
/// file under LocalAppData so the window can be reopened without a new login.
/// </summary>
public sealed class SessionStore
{
    private sealed record SavedSession(string Token, User User, long? LastConversationId = null);

    // Read leniently so a cache file written by another tool (or an older
    // build that used camelCase) still restores.
    private static readonly JsonSerializerOptions CacheOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MoonChat",
        "session.json");

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Token { get; private set; } = "";

    public User? User { get; private set; }

    public bool IsSignedIn => !string.IsNullOrEmpty(Token) && User is not null;

    /// <summary>
    /// Conversation to reopen on the next launch, so returning to the app lands
    /// where the user left off.
    /// </summary>
    public long? LastConversationId { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Why the session ended, when it was not the user asking. The sign-in
    /// screen shows this instead of appearing with no explanation.
    /// </summary>
    public string? EndedReason { get; private set; }

    /// <summary>
    /// Reads a previously saved session, if any.
    ///
    /// Deliberately synchronous: this runs during start-up before Avalonia's
    /// message loop is pumping, and awaiting here would deadlock because the
    /// continuation posts to a UI SynchronizationContext nothing is servicing.
    /// The file is a few hundred bytes of local JSON.
    /// </summary>
    public void Restore()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            var saved = JsonSerializer.Deserialize<SavedSession>(json, CacheOptions);
            if (saved is null || string.IsNullOrEmpty(saved.Token))
            {
                return;
            }

            Token = saved.Token;
            User = saved.User;
            LastConversationId = saved.LastConversationId;
        }
        catch (Exception)
        {
            // A corrupt or unreadable cache should never block startup.
            Token = "";
            User = null;
            LastConversationId = null;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Throws away any session already loaded into memory but leaves the cache
    /// file alone.
    ///
    /// Used when another instance owns the session: the cached token is still
    /// that window's to restore, and deleting the file would sign the owner out
    /// the next time it restarts.
    /// </summary>
    public void DiscardRestored()
    {
        Token = "";
        User = null;
        LastConversationId = null;
        Changed?.Invoke();
    }

    public async Task SaveAsync(string token, User user)
    {
        await _gate.WaitAsync();
        try
        {
            Token = token;
            User = user;
            EndedReason = null;

            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new SavedSession(token, user, LastConversationId), WriteOptions);
            await File.WriteAllTextAsync(_path, json);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    /// <summary>Replaces the token without touching the cached profile.</summary>
    public void UpdateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token == Token)
        {
            return;
        }
        Token = token;
        _ = SaveAsync(token, User!);
    }

    /// <summary>Remembers the conversation to reopen next time.</summary>
    public void RememberConversation(long conversationId)
    {
        if (LastConversationId == conversationId)
        {
            return;
        }

        LastConversationId = conversationId;
        if (IsSignedIn)
        {
            _ = SaveAsync(Token, User!);
        }
    }

    /// <summary>
    /// Drops the session because the server rejected the token, recording why so
    /// the sign-in screen can explain itself.
    ///
    /// The cached file goes too: the token is worthless now, and leaving it
    /// behind would bounce the next launch straight back into a failed session.
    /// </summary>
    public void Expire(string reason)
    {
        EndedReason = reason;
        Clear();
    }

    public void Clear()
    {
        Token = "";
        User = null;
        LastConversationId = null;
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception)
        {
            // Losing the cache file is harmless.
        }
        Changed?.Invoke();
    }
}