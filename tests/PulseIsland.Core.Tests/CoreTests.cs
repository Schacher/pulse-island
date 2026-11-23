using System.Globalization;
using PulseIsland.Core.Input;
using PulseIsland.Core.Localization;
using PulseIsland.Core.Settings;

namespace PulseIsland.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+V", Hotkey.ModControl | Hotkey.ModAlt, 0x56u)]
    [InlineData(" ctrl + shift + f12 ", Hotkey.ModControl | Hotkey.ModShift, 0x7Bu)]
    [InlineData("Win+Alt+1", Hotkey.ModWin | Hotkey.ModAlt, 0x31u)]
    [InlineData("Alt+Space", Hotkey.ModAlt, 0x20u)]
    public void Parses(string text, uint mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(new Hotkey(mods, vk), h);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("V")]          // no modifier
    [InlineData("Ctrl+")]      // no key
    [InlineData("Ctrl+A+B")]   // two keys
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Banana")]
    public void Rejects(string? text) => Assert.False(Hotkey.TryParse(text, out _));
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pulse-test-").FullName;
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = new SettingsStore(SettingsPath).Load();
        Assert.Equal("Ctrl+Alt+V", s.MixerHotkey);
        Assert.Equal(["gaming", "meeting", "focus", "night"], s.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void RoundTripsAndLeavesNoTempFile()
    {
        var store = new SettingsStore(SettingsPath);
        var s = new AppSettings { Language = "tr-TR", MicMuteHotkey = null };
        s.Profiles[0].AppVolumes["spotify.exe"] = 0.42f;
        s.Profiles[0].OutputDeviceId = "{0.0.0.00000000}.{abc}";
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal("tr-TR", loaded.Language);
        Assert.Null(loaded.MicMuteHotkey);
        Assert.Equal(0.42f, loaded.Profiles[0].AppVolumes["spotify.exe"]);
        Assert.Equal("{0.0.0.00000000}.{abc}", loaded.Profiles[0].OutputDeviceId);
        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void CorruptFileIsBackedUpAndDefaultsLoaded()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");
        var s = new SettingsStore(SettingsPath).Load();
        Assert.Equal("Ctrl+Alt+V", s.MixerHotkey);
        Assert.False(File.Exists(SettingsPath));
        var backup = Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.Equal("{ this is not json", File.ReadAllText(backup));
    }

    [Fact]
    public void NullsInEditedFileAreRepaired()
    {
        File.WriteAllText(SettingsPath, """{ "MixerHotkey": null, "Profiles": [ { "Id": "x", "AppVolumes": null } ], "BrokenFeatures": null }""");
        var s = new SettingsStore(SettingsPath).Load();
        Assert.Equal("Ctrl+Alt+V", s.MixerHotkey);
        Assert.NotNull(s.Profiles[0].AppVolumes);
        Assert.NotNull(s.BrokenFeatures);
    }

    [Fact]
    public void ImportRejectsInvalidFile()
    {
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "<xml/>");
        Assert.Null(SettingsStore.TryImport(bad));
        Assert.Null(SettingsStore.TryImport(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void ExportThenImport()
    {
        var target = Path.Combine(_dir, "backup", "pulse.json");
        new SettingsStore(SettingsPath).Export(new AppSettings { Language = "en-US" }, target);
        Assert.Equal("en-US", SettingsStore.TryImport(target)?.Language);
    }
}

public class FeatureGateTests
{
    [Fact]
    public void BrokenOnlyForSameBuildAndVersion()
    {
        var s = new AppSettings();
        Assert.False(FeatureGate.IsBroken(s, FeatureGate.DefaultDevice, "26300.1", "0.1.0"));

        FeatureGate.MarkBroken(s, FeatureGate.DefaultDevice, "26300.1", "0.1.0", "E_NOINTERFACE");
        Assert.True(FeatureGate.IsBroken(s, FeatureGate.DefaultDevice, "26300.1", "0.1.0"));

        // Windows update → retried, stale entry dropped.
        Assert.False(FeatureGate.IsBroken(s, FeatureGate.DefaultDevice, "26300.2", "0.1.0"));
        Assert.Empty(s.BrokenFeatures);

        // App update → retried too.
        FeatureGate.MarkBroken(s, FeatureGate.DefaultDevice, "26300.2", "0.1.0", "x");
        Assert.False(FeatureGate.IsBroken(s, FeatureGate.DefaultDevice, "26300.2", "0.2.0"));
    }
}

public class LocTests
{
    [Fact]
    public void EveryLanguageHasEveryEnglishKey()
    {
        var english = Loc.Load("en-US")!;
        foreach (var code in Loc.Available)
        {
            var missing = english.Keys.Except(Loc.Load(code)!.Keys).ToList();
            Assert.True(missing.Count == 0, $"{code} is missing: {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void PlaceholdersMatchEnglish()
    {
        var english = Loc.Load("en-US")!;
        foreach (var code in Loc.Available)
            foreach (var (key, value) in Loc.Load(code)!)
                Assert.True(english[key].Contains("{0}") == value.Contains("{0}"), $"{code}/{key} placeholder mismatch");
    }

    [Theory]
    [InlineData(null, "tr-TR", "tr-TR")]
    [InlineData(null, "tr", "tr-TR")]
    [InlineData(null, "en-GB", "en-US")]
    [InlineData(null, "pt-BR", "en-US")]   // unsupported → English
    [InlineData("en-US", "tr-TR", "en-US")] // explicit setting wins
    [InlineData("xx-XX", "tr-TR", "tr-TR")] // unknown setting → OS
    public void Resolves(string? setting, string culture, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, new CultureInfo(culture)));

    [Fact]
    public void SwitchesAndFallsBack()
    {
        Loc.SetLanguage("tr-TR");
        Assert.Equal("Mikrofon", Loc.T("Mixer.Input"));
        Assert.Equal("Missing.Key", Loc.T("Missing.Key"));
        Loc.SetLanguage("xx-XX");
        Assert.Equal("en-US", Loc.Current);
        Assert.Equal("Microphone", Loc.T("Mixer.Input"));
    }
}
