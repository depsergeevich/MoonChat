using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace MoonChat.Client.Services;

/// <summary>
/// Downloads and caches avatars as in-memory bitmaps.
///
/// Avatars cannot simply be pointed at by URL the way an HTML image tag could:
/// the endpoint requires the bearer token, which a plain image request cannot
/// carry. Fetching the bytes through the authenticated <see cref="HttpClient"/>
/// solves that and gives us caching and a stable bitmap for binding.
/// </summary>
public sealed class AvatarCache : IDisposable
{
    private readonly ApiClient _api;
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inFlight = new(StringComparer.Ordinal);
    private readonly List<Bitmap> _bitmaps = [];
    private readonly object _gate = new();

    public AvatarCache(ApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Returns the cached bitmap for an avatar path, downloading it on first
    /// use. Concurrent requests for the same path share one download.
    /// </summary>
    public Task<Bitmap?> GetAsync(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        // ConcurrentDictionary only gives us a single download per key if we
        // store the task itself rather than the completed bitmap.
        return _inFlight.GetOrAdd(avatarUrl, url => DownloadAsync(url));
    }

    private async Task<Bitmap?> DownloadAsync(string avatarUrl)
    {
        try
        {
            var bytes = await _api.DownloadAvatarAsync(avatarUrl);
            if (bytes.Length == 0)
            {
                return null;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);

            lock (_gate)
            {
                _bitmaps.Add(bitmap);
            }

            return bitmap;
        }
        catch (Exception)
        {
            // A missing or malformed avatar must never break the list it
            // appears in; the caller falls back to an initial.
            return null;
        }
    }

    /// <summary>Drops cached images, e.g. after the signed-in user changes.</summary>
    public void Clear()
    {
        List<Bitmap> stale;
        lock (_gate)
        {
            stale = [.. _bitmaps];
            _bitmaps.Clear();
        }

        _inFlight.Clear();

        foreach (var bitmap in stale)
        {
            bitmap.Dispose();
        }
    }

    public void Dispose() => Clear();
}