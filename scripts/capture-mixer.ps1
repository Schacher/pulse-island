# Dev helper: saves a PNG of the visible Pulse Island mixer window (works on any DPI).
param([string]$Out = "$env:TEMP\pulse-mixer.png")
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PulseCap {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int s);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
$h = [PulseCap]::FindWindowW('WinUIDesktopWin32WindowClass', 'Pulse Island')
if ($h -eq [IntPtr]::Zero) { throw 'mixer window not found' }
$r = New-Object PulseCap+RECT
[void][PulseCap]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) # DWMWA_EXTENDED_FRAME_BOUNDS (physical px)
$bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
[void][PulseCap]::PrintWindow($h, $hdc, 2) # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $bmp.Save($Out)
"$Out ($($r.R - $r.L)x$($r.B - $r.T) at $($r.L),$($r.T))"
