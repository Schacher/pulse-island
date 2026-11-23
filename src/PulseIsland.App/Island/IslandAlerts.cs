namespace PulseIsland.Island;

/// <summary>Short status pill: volume, mic, lock keys. Value 0..1 draws a bar; null shows Text.</summary>
internal sealed record OsdPayload(string Glyph, string Text, double? Value = null);

/// <summary>A card with up to two buttons: reminders, Pomodoro done, device connected.</summary>
internal sealed record CardPayload(
    string Glyph,
    string Title,
    string? Subtitle = null,
    CardAction? Primary = null,
    CardAction? Secondary = null,
    Windows.UI.Color? Color = null);

internal sealed record CardAction(string Label, Action Run);
