using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MoonChat.Client.Services;

namespace MoonChat.Client.Controls;

/// <summary>
/// Round avatar that shows the stored image when there is one and the person's
/// initial on a stable colour otherwise.
/// </summary>
public partial class AvatarView : UserControl
{
    public static readonly StyledProperty<string?> AvatarUrlProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(AvatarUrl));

    public static readonly StyledProperty<string> DisplayNameProperty =
        AvaloniaProperty.Register<AvatarView, string>(nameof(DisplayName), string.Empty);

    public static readonly StyledProperty<long> UserIdProperty =
        AvaloniaProperty.Register<AvatarView, long>(nameof(UserId));

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<AvatarView, double>(nameof(Diameter), 40d);

    private AvatarCache? _cache;
    private string? _loadedUrl;

    public AvatarView()
    {
        InitializeComponent();
        ApplySize();
    }

    /// <summary>Set by the view so the control can fetch avatars on demand.</summary>
    public AvatarCache? Cache
    {
        get => _cache;
        set
        {
            _cache = value;
            _loadedUrl = null;
            _ = UpdateAsync();
        }
    }

    /// <summary>
    /// Falls back to the cache published by <see cref="App"/>. Avatar views are
    /// created inside data templates, so they cannot get it injected directly.
    /// </summary>
    private void EnsureCache()
    {
        if (_cache is not null)
        {
            return;
        }

        if (Application.Current is App { Services: { } services })
        {
            _cache = services.GetService(typeof(AvatarCache)) as AvatarCache;
        }
    }

    public string? AvatarUrl
    {
        get => GetValue(AvatarUrlProperty);
        set => SetValue(AvatarUrlProperty, value);
    }

    public string DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public long UserId
    {
        get => GetValue(UserIdProperty);
        set => SetValue(UserIdProperty, value);
    }

    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DiameterProperty)
        {
            ApplySize();
        }

        if (change.Property == DisplayNameProperty || change.Property == UserIdProperty)
        {
            InitialText.Text = Initial;
            InitialBlock.Background = FallbackBrush;
        }

        if (change.Property == AvatarUrlProperty)
        {
            _ = UpdateAsync();
        }
    }

    /// <summary>
    /// Sizes the control and rounds the clip. Values set from XAML arrive after
    /// the constructor, so this runs both there and on every change.
    /// </summary>
    private void ApplySize()
    {
        var size = Math.Max(1, Diameter);

        Width = size;
        Height = size;
        InitialText.FontSize = Math.Max(10, size * 0.38);

        ClipBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(size / 2));
    }

    private string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName[..1].ToUpperInvariant();

    /// <summary>Stable colour per user, so the same person always looks the same.</summary>
    private IBrush FallbackBrush
    {
        get
        {
            string[] palette =
            [
                "#5B8DEF", "#E2725B", "#3FB8A0", "#B073D6", "#D9A441",
                "#5AA9E6", "#D6679C", "#6BBF59", "#C96B4B", "#7A8BD0",
            ];

            var index = (int)(Math.Abs(UserId) % palette.Length);
            return new SolidColorBrush(Color.Parse(palette[index]));
        }
    }

    private async Task UpdateAsync()
    {
        EnsureCache();

        var url = AvatarUrl;

        // Nothing to fetch, or the same image is already on screen.
        if (string.IsNullOrWhiteSpace(url) || url == _loadedUrl || _cache is null)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                PhotoImage.IsVisible = false;
                InitialBlock.IsVisible = true;
                _loadedUrl = null;
            }

            return;
        }

        var bitmap = await _cache.GetAsync(url);
        if (bitmap is null || AvatarUrl != url)
        {
            return;
        }

        _loadedUrl = url;
        PhotoImage.Source = bitmap;
        PhotoImage.IsVisible = true;
        InitialBlock.IsVisible = false;
    }
}