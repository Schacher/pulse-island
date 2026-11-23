using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Media;
using PulseIsland.Core.Wellness;
using PulseIsland.Mixer;

namespace PulseIsland.Island;

/// <summary>OSD, card, cleaning and focus-page state of the island.</summary>
public sealed partial class IslandViewModel
{
    private static readonly SolidColorBrush DotOn = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush DotOff = new(Windows.UI.Color.FromArgb(85, 255, 255, 255));
    private static readonly Windows.UI.Color WorkColor = Windows.UI.Color.FromArgb(255, 255, 112, 67);
    private static readonly Windows.UI.Color BreakColor = Windows.UI.Color.FromArgb(255, 102, 187, 106);

    // ---- expanded pages ----
    private bool _focusPage;
    public Visibility MediaPageVisibility => Show(!_focusPage);
    public Visibility FocusPageVisibility => Show(_focusPage);
    public Brush MediaDotBrush => _focusPage ? DotOff : DotOn;
    public Brush FocusDotBrush => _focusPage ? DotOn : DotOff;
    public void ShowMediaPage() => SetPage(false);
    public void ShowFocusPage() => SetPage(true);

    private void SetPage(bool focus)
    {
        if (_focusPage == focus) return;
        _focusPage = focus;
        foreach (var p in new[] { nameof(MediaPageVisibility), nameof(FocusPageVisibility), nameof(MediaDotBrush), nameof(FocusDotBrush) }) Raise(p);
    }

    // ---- volume of the playing app (its mixer row, shared with the mixer flyout) ----
    private AppGroupViewModel? _mediaApp;
    public AppGroupViewModel? MediaApp => _mediaApp;
    public Visibility MediaAppVisibility => Show(_mediaApp != null);
    public string MediaAppVolumeText => _mediaApp == null ? "" : $"{_mediaApp.Volume:0}";
    public string MediaAppMuteText => Loc.T("Mixer.Mute");
    public void ToggleMediaAppMute() => _mediaApp?.ToggleMute();

    internal void SetMediaApp(AppGroupViewModel? app)
    {
        if (ReferenceEquals(app, _mediaApp)) return;
        if (_mediaApp != null) _mediaApp.PropertyChanged -= OnMediaAppChanged;
        _mediaApp = app;
        if (_mediaApp != null) _mediaApp.PropertyChanged += OnMediaAppChanged;
        Raise(nameof(MediaApp));
        Raise(nameof(MediaAppVisibility));
        Raise(nameof(MediaAppVolumeText));
    }

    private void OnMediaAppChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppGroupViewModel.Volume)) Raise(nameof(MediaAppVolumeText));
    }

    // ---- OSD ----
    private OsdPayload _osd = new("", "");
    public string OsdGlyph => _osd.Glyph;
    public string OsdText => _osd.Text;
    public double OsdValue => (_osd.Value ?? 0) * 100;
    public string OsdValueText => _osd.Value is { } v ? $"{v * 100:0}" : "";
    public Visibility OsdBarVisibility => Show(_osd.Value != null);
    public Visibility OsdTextVisibility => Show(_osd.Value == null);

    internal void ShowOsd(OsdPayload osd)
    {
        _osd = osd;
        foreach (var p in new[] { nameof(OsdGlyph), nameof(OsdText), nameof(OsdValue), nameof(OsdValueText), nameof(OsdBarVisibility), nameof(OsdTextVisibility) }) Raise(p);
    }

    // ---- card ----
    private CardPayload _card = new("", "");
    private Action? _dismissCard;
    private static readonly FontFamily FluentFont = new("Segoe Fluent Icons"), EmojiFont = new("Segoe UI Emoji");
    public string CardGlyph => _card.Glyph;
    /// <summary>Fluent icons live in the private-use area; anything else is colour emoji.</summary>
    public FontFamily CardGlyphFont => _card.Glyph.Length == 1 && _card.Glyph[0] is >= '' and <= '' ? FluentFont : EmojiFont;
    public string CardTitle => _card.Title;
    public string CardSubtitle => _card.Subtitle ?? "";
    public Visibility CardSubtitleVisibility => Show(!string.IsNullOrEmpty(_card.Subtitle));
    public string CardPrimaryText => _card.Primary?.Label ?? "";
    public string CardSecondaryText => _card.Secondary?.Label ?? "";
    public Visibility CardPrimaryVisibility => Show(_card.Primary != null);
    public Visibility CardSecondaryVisibility => Show(_card.Secondary != null);
    public SolidColorBrush CardBrush { get; } = new(Windows.UI.Color.FromArgb(255, 58, 58, 64));

    public void CardPrimary() => RunCardAction(_card.Primary);
    public void CardSecondary() => RunCardAction(_card.Secondary);

    internal void ShowCard(CardPayload card, Action dismiss)
    {
        _card = card;
        _dismissCard = dismiss;
        CardBrush.Color = card.Color ?? Windows.UI.Color.FromArgb(255, 58, 58, 64);
        foreach (var p in new[] { nameof(CardGlyph), nameof(CardGlyphFont), nameof(CardTitle), nameof(CardSubtitle), nameof(CardSubtitleVisibility),
                     nameof(CardPrimaryText), nameof(CardSecondaryText), nameof(CardPrimaryVisibility), nameof(CardSecondaryVisibility) }) Raise(p);
    }

    private void RunCardAction(CardAction? action)
    {
        action?.Run();
        _dismissCard?.Invoke();
    }

    // ---- Pomodoro ----
    internal Action? PomodoroStartPauseRequested, PomodoroSkipRequested, PomodoroStopRequested;
    /// <summary>Change the focus length by this many minutes (idle only).</summary>
    internal Action<int>? PomodoroLengthChangeRequested;
    public Visibility PomodoroIdleVisibility => Show(!_pomodoroActive);
    public void PomodoroShorter() => PomodoroLengthChangeRequested?.Invoke(-5);
    public void PomodoroLonger() => PomodoroLengthChangeRequested?.Invoke(5);
    private bool _pomodoroActive, _pomodoroRunning;
    private string _pomodoroTime = "25:00";
    private double _pomodoroProgress;
    private PomodoroPhase _phase;

    public bool PomodoroActive => _pomodoroActive;
    public Visibility PomodoroVisibility => Show(_pomodoroActive);
    public Visibility CompactArtVisibility => Show(!_pomodoroActive);
    public string PomodoroTimeText => _pomodoroTime;
    public double PomodoroProgress => _pomodoroProgress;
    public string PomodoroPhaseText => Loc.T(_pomodoroActive ? $"Pomodoro.{_phase}" : "Pomodoro.Ready");
    public string PomodoroPlayGlyph => _pomodoroRunning ? "" : "";
    public string PomodoroStartText => Loc.T(_pomodoroRunning ? "Pomodoro.Pause" : _pomodoroActive ? "Pomodoro.Resume" : "Pomodoro.Start");
    public string PomodoroSkipText => Loc.T("Pomodoro.Skip");
    public string PomodoroStopText => Loc.T("Pomodoro.Stop");
    public SolidColorBrush PomodoroBrush { get; } = new(WorkColor);

    public void PomodoroStartPause() => PomodoroStartPauseRequested?.Invoke();
    public void PomodoroSkip() => PomodoroSkipRequested?.Invoke();
    public void PomodoroStop() => PomodoroStopRequested?.Invoke();

    internal void SetPomodoro(bool active, bool running, PomodoroPhase phase, TimeSpan remaining, double progress)
    {
        var activeChanged = active != _pomodoroActive;
        _pomodoroActive = active;
        _pomodoroRunning = running;
        _phase = phase;
        _pomodoroTime = MediaTimeline.Format(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds)));
        _pomodoroProgress = progress * 100;
        PomodoroBrush.Color = phase == PomodoroPhase.Work ? WorkColor : BreakColor;
        foreach (var p in new[] { nameof(PomodoroTimeText), nameof(PomodoroProgress), nameof(PomodoroPhaseText), nameof(PomodoroPlayGlyph), nameof(PomodoroStartText) }) Raise(p);
        if (activeChanged)
            foreach (var p in new[] { nameof(PomodoroActive), nameof(PomodoroVisibility), nameof(PomodoroIdleVisibility), nameof(CompactArtVisibility) }) Raise(p);
    }

    // ---- water ----
    internal Action? AddWaterRequested, RemoveWaterRequested, ResetWaterRequested;
    public string WaterRemoveText => Loc.T("Water.Remove");
    public string WaterResetText => Loc.T("Water.Reset");
    public void RemoveWater() => RemoveWaterRequested?.Invoke();
    public void ResetWater() => ResetWaterRequested?.Invoke();
    private string _waterText = "", _waterAdd = "";
    private double _waterProgress;
    public string WaterText => _waterText;
    public string WaterAddText => _waterAdd;
    public double WaterProgress => _waterProgress;
    public void AddWater() => AddWaterRequested?.Invoke();

    internal void SetWater(int consumedMl, int goalMl, int glassMl, string unit)
    {
        _waterText = Loc.T("Water.Progress", $"{FormatVolume(consumedMl, unit)} / {FormatVolume(goalMl, unit)}");
        _waterAdd = "+" + FormatVolume(glassMl, unit);
        _waterProgress = Math.Min(100, consumedMl * 100.0 / Math.Max(1, goalMl));
        Raise(nameof(WaterText));
        Raise(nameof(WaterAddText));
        Raise(nameof(WaterProgress));
    }

    internal static string FormatVolume(int ml, string unit) =>
        unit == "oz" ? $"{ml / 29.5735:0} oz" : $"{ml} ml";

    // ---- cleaning ----
    internal Action? CleaningRequested, UnlockRequested;
    private string _cleaningRemaining = "", _cleaningButton = "";
    private double _cleaningProgress, _cleaningHold;
    public string CleaningTitle => Loc.T("Cleaning.Title");
    public string CleaningHint => Loc.T("Cleaning.Hint");
    public string CleaningSlideText => Loc.T("Cleaning.Slide");
    public string CleaningRemainingText => _cleaningRemaining;
    public double CleaningProgress => _cleaningProgress;
    public double CleaningHoldProgress => _cleaningHold;
    public string CleaningButtonText => _cleaningButton;
    public void StartCleaning() => CleaningRequested?.Invoke();

    internal void SetCleaning(TimeSpan remaining, TimeSpan total)
    {
        _cleaningRemaining = Loc.T("Cleaning.Remaining", MediaTimeline.Format(TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds))));
        _cleaningProgress = total > TimeSpan.Zero ? (1 - remaining / total) * 100 : 0;
        Raise(nameof(CleaningRemainingText));
        Raise(nameof(CleaningProgress));
    }

    internal void SetCleaningHold(double progress)
    {
        _cleaningHold = progress * 100;
        Raise(nameof(CleaningHoldProgress));
    }

    internal void SetCleaningButton(int minutes)
    {
        _cleaningButton = Loc.T("Cleaning.Button", minutes);
        Raise(nameof(CleaningButtonText));
    }

    internal void RefreshPanelTexts()
    {
        foreach (var p in new[] { nameof(PomodoroPhaseText), nameof(PomodoroStartText), nameof(PomodoroSkipText), nameof(PomodoroStopText),
                     nameof(CleaningTitle), nameof(CleaningHint), nameof(CleaningSlideText), nameof(WaterRemoveText), nameof(WaterResetText) }) Raise(p);
    }
}
