# Dev helper: PNG of a top-level window by title (screen pixels, includes layered windows).
param([Parameter(Mandatory)][string]$Title, [string]$Out = "$env:TEMP\pulse-window.png")
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PulseWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int s);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
[void][PulseWin]::SetProcessDpiAwarenessContext([IntPtr]-4)
$h = [PulseWin]::FindWindowW("WinUIDesktopWin32WindowClass", $Title) # $null would arrive as "" and match nothing
if ($h -eq [IntPtr]::Zero) { throw "window '$Title' not found" }
$r = New-Object PulseWin+RECT
[void][PulseWin]::DwmGetWindowAttribute($h, 9, [ref]$r, 16)
& (Join-Path $PSScriptRoot 'capture-screen.ps1') -X $r.L -Y $r.T -Width ($r.R - $r.L) -Height ($r.B - $r.T) -Out $Out
