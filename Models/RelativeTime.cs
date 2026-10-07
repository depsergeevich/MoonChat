namespace MoonChat.Client.Models;

/// <summary>
/// Turns a unix-millisecond timestamp into the short "3m ago" phrasing used
/// next to contact names. Kept out of the DTOs themselves because it is
/// presentation, not part of the wire contract.
/// </summary>
public static class RelativeTime
{
    /// <summary>
    /// Describes how long ago <paramref name="unixMilliseconds"/> happened, e.g.
    /// "a moment ago", "5m ago", "3h ago", "2d ago". Anything older than a week
    /// falls back to a date, and 0 means "never".
    /// </summary>
    public static string Describe(long unixMilliseconds)
    {
        if (unixMilliseconds <= 0)
        {
            return string.Empty;
        }

        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);

        // A clock skew that puts the timestamp in the future should read as
        // "just now" rather than a negative age.
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "a moment ago";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime().ToString("d MMM");
    }
}