# Dev helper: PNG of a screen region in physical pixels. Uses BitBlt with CAPTUREBLT, which is needed
# to include layered/transparent windows such as the island (CopyFromScreen leaves them out).
param([int]$X = 0, [int]$Y = 0, [int]$Width = 1000, [int]$Height = 300, [string]$Out = "$env:TEMP\pulse-screen.png")
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PulseScreen {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
  [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
}
'@
[void][PulseScreen]::SetProcessDpiAwarenessContext([IntPtr]-4) # per-monitor v2: physical coordinates
$bmp = New-Object System.Drawing.Bitmap $Width, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dst = $g.GetHdc(); $src = [PulseScreen]::GetDC([IntPtr]::Zero)
[void][PulseScreen]::BitBlt($dst, 0, 0, $Width, $Height, $src, $X, $Y, 0x00CC0020 -bor 0x40000000) # SRCCOPY | CAPTUREBLT
[void][PulseScreen]::ReleaseDC([IntPtr]::Zero, $src); $g.ReleaseHdc($dst)
$bmp.Save($Out)
$Out
