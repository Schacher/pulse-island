# Dev helper: a MUTED media session (no sound) so the island's Now Playing can be tested.
# Needs Windows PowerShell 5.1 (WinRT projection):  powershell -File scripts\fake-media.ps1 -Seconds 60
# -ArtDelay N: send the artwork N seconds after the title, like Chromium browsers do.
param([int]$Seconds = 60, [string]$Title = 'Test Song', [string]$Artist = 'Pulse Island',
      [string]$Art = 'C:\Windows\Web\Wallpaper\Windows\img0.jpg', [int]$ArtDelay = 0,
      [switch]$Audible) # -Audible: really plays the sound (only use on a muted output device)
[void][Windows.Media.Playback.MediaPlayer, Windows.Media, ContentType = WindowsRuntime]
[void][Windows.Media.Core.MediaSource, Windows.Media, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.RandomAccessStreamReference, Windows.Storage.Streams, ContentType = WindowsRuntime]

$player = New-Object Windows.Media.Playback.MediaPlayer
$player.IsMuted = -not $Audible
$player.Volume = if ($Audible) { 1 } else { 0 }
$player.IsLoopingEnabled = $true
[void][Windows.Media.Playback.MediaPlaybackItem, Windows.Media, ContentType = WindowsRuntime]
$item = New-Object Windows.Media.Playback.MediaPlaybackItem ([Windows.Media.Core.MediaSource]::CreateFromUri([Uri]'file:///C:/Windows/Media/Alarm01.wav'))
# MediaPlayer drives the system media controls from the item's display properties (a manual DisplayUpdater gets overwritten).
$props = $item.GetDisplayProperties()
$props.Type = [Windows.Media.MediaPlaybackType]::Music
$props.MusicProperties.Title = $Title
$props.MusicProperties.Artist = $Artist
function Set-Art {
    if (-not (Test-Path $Art)) { return }
    # A file-backed reference (like real players send); a file:// URI is silently dropped by SMTC.
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    [void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    $task = $asTask.MakeGenericMethod([Windows.Storage.StorageFile]).Invoke($null, @([Windows.Storage.StorageFile]::GetFileFromPathAsync($Art)))
    $props.Thumbnail = [Windows.Storage.Streams.RandomAccessStreamReference]::CreateFromFile($task.Result)
    $item.ApplyDisplayProperties($props)
}
if ($ArtDelay -eq 0) { Set-Art }
$item.ApplyDisplayProperties($props)
$player.Source = $item

$player.Play()
if ($ArtDelay -gt 0) { Start-Sleep -Seconds $ArtDelay; Set-Art; $Seconds -= $ArtDelay }
Start-Sleep -Seconds $Seconds
$player.Dispose()
