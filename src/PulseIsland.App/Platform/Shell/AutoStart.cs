using Microsoft.Win32;

namespace PulseIsland.Platform.Shell;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights). The registry is the source of truth.</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PulseIsland";
    public const string Argument = "--autostart";

    private static string Command => $"\"{Environment.ProcessPath}\" {Argument}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Portable app: if the folder was moved, point the Run entry at the new exe.</summary>
    public static void RepairPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != Command)
            key.SetValue(ValueName, Command);
    }
}
