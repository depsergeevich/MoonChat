using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MoonChat.Client.Models;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for the few DTOs whose
/// fields change while the UI is live.
///
/// Without this, presence updates mutated plain objects and the bindings in the
/// contact list and conversation rows silently kept the values they first read.
/// Only the properties that actually change at runtime are routed through
/// <see cref="SetProperty"/>; the rest stay plain auto-properties and cost
/// nothing.
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> and raises a change notification when it
    /// differs from <paramref name="field"/>.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>Raises a change notification for a computed property.</summary>
    protected void Raise(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}