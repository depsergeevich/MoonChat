namespace MoonChat.Client.Services;

/// <summary>
/// One signed-in MoonChat window per machine session.
///
/// The first instance to start takes a named lock and may restore the cached
/// session. Any instance that starts while the lock is held comes up signed out
/// instead, so two windows never race each other over the same session file or
/// quietly duplicate a login.
///
/// The lock lives as long as the process. If the owner is killed the operating
/// system drops it, so a later launch becomes the owner without any stale-state
/// cleanup.
/// </summary>
public sealed class SingleSessionGuard : IDisposable
{
    // Local\ scopes the name to this Windows logon session, so two people signed
    // in to the same PC each get their own window.
    private const string MutexName = @"Local\MoonChat.Session";

    private readonly Mutex? _handle;
    private readonly bool _isOwner;

    private SingleSessionGuard(Mutex? handle, bool isOwner)
    {
        _handle = handle;
        _isOwner = isOwner;
    }

    /// <summary>True when this process took the lock and owns the cached session.</summary>
    public bool IsOwner => _isOwner;

    /// <summary>
    /// Takes the lock if it is free. Always returns a guard: check
    /// <see cref="IsOwner"/> to find out whether another instance got there first.
    /// </summary>
    public static SingleSessionGuard TryAcquire()
    {
        try
        {
            // When the name already exists the constructor still hands back a
            // handle, but this thread does not own it, hence createdNew.
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            return new SingleSessionGuard(mutex, createdNew);
        }
        catch (Exception)
        {
            // If the lock cannot be created at all, fail open. Being unable to
            // prove a duplicate should never lock the user out of the app.
            return new SingleSessionGuard(handle: null, isOwner: true);
        }
    }

    public void Dispose()
    {
        if (_handle is null)
        {
            return;
        }

        if (_isOwner)
        {
            try
            {
                _handle.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned on this thread, or already abandoned by a crash.
            }
        }

        _handle.Dispose();
    }
}