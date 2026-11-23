namespace PulseIsland.Core.Input;

/// <summary>A global hotkey in RegisterHotKey terms (MOD_* flags + virtual-key code).</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    /// <summary>Parses "Ctrl+Alt+V", "ctrl + shift + F12", "Win+Alt+1". At least one modifier is required.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToUpperInvariant();
            uint mod = part switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0,
            };
            if (mod != 0) { mods |= mod; continue; }
            if (vk != 0) return false; // two non-modifier keys
            vk = KeyCode(part);
            if (vk == 0) return false;
        }
        if (mods == 0 || vk == 0) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    private static uint KeyCode(string k)
    {
        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z' or >= '0' and <= '9') return k[0]; // VK_A.. / VK_0.. equal ASCII
        if (k.Length is 2 or 3 && k[0] == 'F' && int.TryParse(k.AsSpan(1), out var n) && n is >= 1 and <= 24)
            return (uint)(0x70 + n - 1); // VK_F1 = 0x70
        return k switch
        {
            "SPACE" => 0x20,
            "PAUSE" => 0x13,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            _ => 0,
        };
    }
}
