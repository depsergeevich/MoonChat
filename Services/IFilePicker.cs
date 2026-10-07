using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MoonChat.Client.Services;

/// <summary>
/// Opens a file picker. Exists so view models can ask for a file without
/// referencing Avalonia's windowing or storage APIs, which would make them
/// impossible to exercise outside a running desktop app.
/// </summary>
public interface IFilePicker
{
    /// <summary>
    /// Shows an image picker and returns an open read stream, or null when the
    /// user cancelled. The caller owns the returned stream.
    /// </summary>
    Task<Stream?> PickImageAsync();
}

/// <summary>
/// Picker backed by the main window's storage provider, which on desktop is the
/// operating system's own file dialog.
/// </summary>
public sealed class DesktopFilePicker : IFilePicker
{
    public async Task<Stream?> PickImageAsync()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow is not TopLevel top)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a photo",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.Count == 0)
        {
            return null;
        }

        try
        {
            // A stream rather than a path: storage items are not guaranteed to
            // live on the local filesystem.
            return await files[0].OpenReadAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }
}