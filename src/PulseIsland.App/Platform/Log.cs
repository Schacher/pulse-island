namespace PulseIsland.Platform;

/// <summary>Local error log (%APPDATA%\PulseIsland\log.txt). Never leaves the machine.</summary>
internal static class Log
{
    private static readonly string s_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseIsland", "log.txt");
    private static readonly Lock s_lock = new();

    public static void Error(string where, Exception e) => Write($"ERROR {where}: {e}");

    public static void Info(string message) => Write($"INFO {message}");

    private static void Write(string line)
    {
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(s_path)!);
                if (File.Exists(s_path) && new FileInfo(s_path).Length > 1_000_000) File.Delete(s_path); // keep it small
                File.AppendAllText(s_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { } // logging must never take the app down
        catch (UnauthorizedAccessException) { }
    }
}
