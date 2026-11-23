using System.Globalization;
using System.Text.Json;
using PulseIsland.Core.Settings;

namespace PulseIsland.Core.Localization;

/// <summary>UI strings. Language can be switched at runtime; callers rebuild their views afterwards.</summary>
public static class Loc
{
    /// <summary>All 8 target languages. Only the ones with a bundled JSON file are <see cref="Available"/>.</summary>
    public static readonly string[] Supported = ["tr-TR", "en-US", "ru-RU", "de-DE", "fr-FR", "it-IT", "zh-CN", "ja-JP"];

    public const string Fallback = "en-US";

    private static Dictionary<string, string> s_current = Load(Fallback)!;
    private static readonly Dictionary<string, string> s_fallback = s_current;

    public static string Current { get; private set; } = Fallback;

    public static IReadOnlyList<string> Available { get; } =
        Supported.Where(c => typeof(Loc).Assembly.GetManifestResourceInfo($"Strings.{c}.json") != null).ToArray();

    public static event Action? LanguageChanged;

    /// <summary>Picks the language to use: explicit setting if available, else the OS culture, else English.</summary>
    public static string Resolve(string? setting, CultureInfo uiCulture)
    {
        if (setting != null && Available.Contains(setting)) return setting;
        for (var c = uiCulture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            var match = Available.FirstOrDefault(a => a.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
                        ?? Available.FirstOrDefault(a => a.StartsWith(c.TwoLetterISOLanguageName + "-", StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return Fallback;
    }

    public static void SetLanguage(string code)
    {
        var strings = Load(code);
        s_current = strings ?? s_fallback;
        Current = strings != null ? code : Fallback;
        LanguageChanged?.Invoke();
    }

    /// <summary>Translated string; falls back to English, then to the key itself.</summary>
    public static string T(string key) =>
        s_current.TryGetValue(key, out var v) ? v : s_fallback.TryGetValue(key, out v) ? v : key;

    public static string T(string key, object arg) => string.Format(CultureInfo.CurrentCulture, T(key), arg);

    /// <summary>Native display name for the language menu.</summary>
    public static string NativeName(string code) => T($"Lang.{code}");

    internal static Dictionary<string, string>? Load(string code)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Strings.{code}.json");
        return stream == null ? null : JsonSerializer.Deserialize(stream, CoreJsonContext.Default.DictionaryStringString);
    }
}
