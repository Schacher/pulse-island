# Pulse Island

A lightweight, fully offline Windows 11 volume mixer in the system tray, plus a Dynamic Island–style
hub at the top of the screen: Now Playing, Pomodoro, health reminders and on-screen indicators.

No accounts, no telemetry, no network access. MIT licensed.

## Dynamic Island

- A pill at the top-center of the screen that morphs with spring animations on the compositor thread
- **Hover to open it**: album art, title/artist, seek bar, previous/play/next and a volume slider for the
  playing app only (Brave, Spotify, …), separate from the Windows volume
- Now Playing from Windows media controls: Spotify, browsers, VLC and any app that reports to Windows
- Glow and accent colour taken from the album art, real audio spectrum while music plays
- Briefly shows the new song when the track changes
- Over fullscreen apps: shrinks to a dot, hides, or keeps showing (with a per-app allow list)
- Main display or the active window's display; position, size, and a black or glass look

## Focus and health

- Second page of the island (page dots): **Pomodoro** (also shown as a ring on the small pill, ±5 min),
  **water log** (+ / − a glass, reset), **keyboard cleaning**
- Reminders as island cards: drink water, 20-20-20 eye rest, stand up (snooze). Being away from the PC
  counts as a break and pauses nudges
- **Keyboard cleaning** (optionally mouse clicks too): unlock by holding Esc + Space for 3 s, sliding the
  knob on the island, or when time is up. Ctrl+Alt+Del always works

## Indicators

Volume (the volume keys are handled by Pulse Island so Windows' own flyout doesn't appear), microphone
mute shortcut, Caps / Num / Scroll Lock, Bluetooth device connected (+ battery when Windows knows it).

## Tray mixer

- Left-click the tray icon or press **Ctrl+Alt+V**
- Master volume and live level, microphone level/mute (**Ctrl+Alt+M**)
- Per-app volume, mute and level (sessions of one app are grouped)
- Switch the default output/input device; profiles (Gaming, Meeting, Focus, Night) you can overwrite

## Settings

Tray menu or mixer ⚙ → **Settings**: language, start with Windows, theme, Lite Mode, shortcuts
(click and press keys), island, Pomodoro, water, breaks, indicators, cleaning, backup/restore.
Quick presets are also in the mixer's ⚙ menu.

**Languages:** English, Türkçe, Русский, Deutsch, Français, Italiano, 简体中文, 日本語 — picked from
Windows on first run, switchable without a restart.

**Lite Mode** turns off springs, glow, fades and the audio spectrum for older PCs.

## Install (portable)

1. Download `PulseIsland-<version>-x64.zip` and extract it anywhere.
2. Run `PulseIsland.exe`. The build is not code-signed, so Windows SmartScreen may warn:
   choose **More info → Run anyway**.

Requires Windows 11 22H2 or later. Settings live in `%APPDATA%\PulseIsland\settings.json`; a local error
log is written to `%APPDATA%\PulseIsland\log.txt`. Nothing is ever sent anywhere.

## Undocumented Windows APIs

Switching the default device uses an undocumented Windows interface (as the Sound control panel does).
If it fails on your Windows build, Pulse Island tells you once, turns just that feature off, and tries
again automatically after the next Windows or app update.

## Known limits

- Idle memory is about 85–90 MB (the island is its own WinUI window). Idle CPU is well under 0.1 %.
- The glass look is translucent but not blurred: Windows only blurs rectangular windows.
- Over exclusive-fullscreen games the island cannot draw (no app can without injecting into the game).
- A browser's volume slider changes all its tabs: Windows has one audio session per browser.
- Brightness indicators and Focus Assist sync are not implemented.

## Build

Requirements: .NET 10 SDK, Visual Studio Build Tools with the C++ workload (for NativeAOT).

```powershell
dotnet test --project tests/PulseIsland.Core.Tests   # unit tests
dotnet build src/PulseIsland.App                      # debug build
./scripts/publish.ps1                                 # NativeAOT portable build → dist/
```

Dev aids for screenshots: `PULSE_DEMO=osd|lock|card|cleaning|focus|morph|settings`,
`PULSE_ISLAND_MODE=Compact|Peek|Expanded`, `PULSE_LANG=de-DE` (not saved).
`scripts/fake-media.ps1` creates a muted media session for testing Now Playing.

## License

MIT
