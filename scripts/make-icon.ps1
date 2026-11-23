# Generates src/PulseIsland.App/Assets/PulseIsland.ico (dark pill with white level bars).
# Run with Windows PowerShell 5.1 (needs System.Drawing): powershell -File scripts\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\PulseIsland.App\Assets\PulseIsland.ico'
$sizes = 16, 20, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    # pill
    $h = [Math]::Max(8, [Math]::Round($s * 0.62)); $y = ($s - $h) / 2; $r = $h
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, $y, $r, $h, 90, 180)
    $path.AddArc($s - $r - 0.01, $y, $r, $h, 270, 180)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 18, 20))), $path)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), ([Math]::Max(1, $s / 32))), $path)
    # level bars
    $levels = 0.35, 0.75, 1.0, 0.55
    $bw = [Math]::Max(1, [Math]::Round($s * 0.07)); $gap = [Math]::Max(1, [Math]::Round($s * 0.06))
    $total = 4 * $bw + 3 * $gap; $x = ($s - $total) / 2
    $maxH = $h * 0.6
    foreach ($l in $levels) {
        $bh = [Math]::Max(1, $maxH * $l)
        $g.FillRectangle([System.Drawing.Brushes]::White, [single]$x, [single](($s - $bh) / 2), [single]$bw, [single]$bh)
        $x += $bw + $gap
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($out); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Output "wrote $out"
