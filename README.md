# Pulse Island

A lightweight, fully offline Windows 11 volume mixer in the system tray, plus a Dynamic Island–style
hub at the top of the screen.

**Status: Milestone 3** — wellness, keyboard cleaning and on-screen indicators on the island.

## Wellness, cleaning and indicators (M3)

- Expanded island → page dots → **Focus** page: Pomodoro (ring + time also on the compact pill), water log, keyboard cleaning
- Reminders as island cards: drink water (+1 glass button), 20-20-20 eye rest, stand up (snooze 10 min).
  Being away from the PC (no input for 5 min) counts as a break and pauses nudges
- Keyboard cleaning (1/2/5 min, optionally mouse clicks too): unlock by holding Esc + Space for 3 s,
  the slider on the island, or when time is up. Ctrl+Alt+Del always works
- Indicators: volume (the volume keys are handled by Pulse Island, so Windows' own flyout doesn't appear),
  microphone mute hotkey, Caps / Num / Scroll Lock, Bluetooth device connected (+ battery when Windows knows it)
- Mixer ⚙ → Health reminders to switch each of these on/off

## Island (M2)

- Pill at the top-center of the screen; spring-animated morphs (on the compositor thread)
- Hover to peek (title/artist), click to expand (art, seek bar, play/pause/next/previous)
- Now Playing from Windows media controls: Spotify, browsers, VLC and any app that reports to Windows
- Glow and accent colour taken from the album art, real audio spectrum (WASAPI loopback, only while playing)
- Briefly shows the new song when the track changes
- Over fullscreen apps: shrinks to a dot (or hides / keeps showing). `FullscreenAllowList` in settings.json
- Main display or the active window's display; offset and scale in settings.json
- Mixer ⚙ menu → Island, or the tray menu, to show/hide and change these

## Tray mixer (M1)

- Tray flyout (left-click the tray icon or press **Ctrl+Alt+V**)
- Master volume, mute and live level meter for the default output
- Microphone level and mute, plus a mute hotkey (**Ctrl+Alt+M**)
- Per-app volume, mute and level meter (sessions of one app are grouped)
- Switch the default output/input device (all roles)
- Profiles: Gaming, Meeting, Focus, Night. Click to apply; right-click → "save current state"
- Spatial sound status, start with Windows, settings export/import
- English and Turkish UI (6 more languages in M4), switchable without a restart

## Install (portable)

1. Download `PulseIsland-<version>-x64.zip` and extract it anywhere.
2. Run `PulseIsland.exe`. The build is not code-signed, so Windows SmartScreen may warn:
   choose **More info → Run anyway**.

Settings live in `%APPDATA%\PulseIsland\settings.json` (hotkeys can be changed there for now).
A local error log is written to `%APPDATA%\PulseIsland\log.txt`. Nothing is ever sent anywhere.

## Undocumented Windows APIs

Switching the default device uses an undocumented Windows interface (as the Sound control panel
does). If it fails on your Windows build, Pulse Island tells you once, turns just that feature off,
and tries again automatically after the next Windows or app update.

## Build

Requirements: .NET 10 SDK, Visual Studio Build Tools with the C++ workload (for NativeAOT).

```powershell
dotnet test --project tests/PulseIsland.Core.Tests   # unit tests
dotnet build src/PulseIsland.App                      # debug build
./scripts/publish.ps1                                 # NativeAOT portable build → dist/
```

## License

MIT
