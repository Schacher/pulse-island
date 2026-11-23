using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Media;
using PulseIsland.Mixer;
using PulseIsland.Platform;
using PulseIsland.Platform.Media;
using Windows.Graphics.Imaging;

namespace PulseIsland.Island;

public enum IslandMode { Hidden, Dot, Idle, Compact, Peek, Expanded, Osd, Card, Cleaning }

/// <summary>What the island's XAML shows. UI thread only.</summary>
public sealed partial class IslandViewModel : Observable
{
    private static readonly Windows.UI.Color DefaultAccent = Windows.UI.Color.FromArgb(255, 120, 200, 255);

    private readonly MediaService _media;
    private IslandMode _mode;
    private NowPlaying? _now;
    private string? _artKey;
    private string? _artTrack;
    private ImageSource? _art;
    private double _position;
    private bool _seeking;

    internal IslandViewModel(MediaService media) => _media = media;

    /// <summary>Raised when the album-art accent changes (null = no colourful art).</summary>
    internal event Action<Windows.UI.Color?>? AccentChanged;

    public SolidColorBrush AccentBrush { get; } = new(DefaultAccent);

    public IslandMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            foreach (var p in new[] { nameof(CompactVisibility), nameof(PeekVisibility), nameof(ExpandedVisibility),
                         nameof(OsdVisibility), nameof(CardVisibility), nameof(CleaningVisibility) }) Raise(p);
        }
    }

    public Visibility CompactVisibility => Show(_mode == IslandMode.Compact);
    public Visibility PeekVisibility => Show(_mode == IslandMode.Peek);
    public Visibility ExpandedVisibility => Show(_mode == IslandMode.Expanded);
    public Visibility OsdVisibility => Show(_mode == IslandMode.Osd);
    public Visibility CardVisibility => Show(_mode == IslandMode.Card);
    public Visibility CleaningVisibility => Show(_mode == IslandMode.Cleaning);

    public bool HasMedia => _now != null;
    public Visibility MediaVisibility => Show(_now != null);
    public Visibility ArtistVisibility => Show(!string.IsNullOrEmpty(_now?.Artist));
    public bool IsPlaying => _now?.IsPlaying == true;
    public string Title => _now?.Title ?? Loc.T("Island.NothingPlaying");
    public string Artist => _now?.Artist ?? "";
    public string PlayGlyph => IsPlaying ? "" : "";
    public bool CanPrevious => _now?.CanPrevious == true;
    public bool CanNext => _now?.CanNext == true;
    public bool CanSeek => _now?.CanSeek == true && DurationSeconds > 0;

    public ImageSource? Art { get => _art; private set { if (Set(ref _art, value)) Raise(nameof(ArtPlaceholderVisibility)); } }
    public Visibility ArtPlaceholderVisibility => Show(_art == null);

    public double DurationSeconds => _now?.Timeline.Duration.TotalSeconds ?? 0;
    public double PositionSeconds { get => _position; private set => Set(ref _position, value); }
    public string PositionText => MediaTimeline.Format(TimeSpan.FromSeconds(_position));
    public string DurationText => MediaTimeline.Format(_now?.Timeline.Duration ?? TimeSpan.Zero);

    public void PlayPause() => _media.TogglePlayPause();
    public void Next() => _media.Next();
    public void Previous() => _media.Previous();

    internal void BeginSeek() => _seeking = true;

    internal void EndSeek(double seconds)
    {
        _seeking = false;
        if (CanSeek) _media.Seek(TimeSpan.FromSeconds(seconds));
    }

    internal void SetMedia(NowPlaying? now)
    {
        _now = now;
        foreach (var p in new[] { nameof(HasMedia), nameof(MediaVisibility), nameof(ArtistVisibility), nameof(IsPlaying), nameof(Title), nameof(Artist), nameof(PlayGlyph),
                     nameof(CanPrevious), nameof(CanNext), nameof(CanSeek), nameof(DurationSeconds), nameof(DurationText) })
            Raise(p);
        TickPosition();
        if (now?.ArtKey != _artKey)
        {
            _artKey = now?.ArtKey;
            LoadArt(now);
        }
    }

    internal void RefreshTexts() => Raise(nameof(Title));

    /// <summary>Moves the seek bar along between the player's (rare) position reports.</summary>
    internal void TickPosition()
    {
        if (_seeking) return;
        PositionSeconds = _now?.Timeline.PositionAt(DateTimeOffset.Now).TotalSeconds ?? 0;
        Raise(nameof(PositionText));
    }

    private async void LoadArt(NowPlaying? now)
    {
        var key = _artKey;
        ImageSource? image = null;
        Windows.UI.Color? accent = null;
        if (now?.Thumbnail != null)
        {
            try
            {
                using (var stream = await now.Thumbnail.OpenReadAsync())
                {
                    // Decode by height: covers are often 16:9 video frames, center-cropped into a square.
                    var bitmap = new BitmapImage { DecodePixelHeight = 128 };
                    await bitmap.SetSourceAsync(stream);
                    image = bitmap;
                }
                using (var stream = await now.Thumbnail.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                        new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24 },
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                    if (AccentColor.Pick(pixels.DetachPixelData()) is { } c)
                        accent = Windows.UI.Color.FromArgb(255, c.R, c.G, c.B);
                }
            }
            catch (Exception e)
            {
                Log.Info($"island: artwork failed: {e.Message}");
            }
        }
        if (now != null && image == null) Log.Info($"island: no artwork for '{now.Title}' (thumbnail {(now.Thumbnail == null ? "missing" : "unreadable")})");
        if (key != _artKey) return; // track changed while loading
        if (image == null && now != null && _art != null && _artTrack == now.TrackKey) return; // keep the art we have for this track
        _artTrack = now?.TrackKey;
        Art = image;
        AccentBrush.Color = accent ?? DefaultAccent;
        AccentChanged?.Invoke(accent);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
