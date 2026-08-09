# FrameHub

[English](README.md) | [Polski](README.pl.md)

![FrameHub logo](FrameHub.App/Assets/FrameHubLogo.png)

FrameHub is an open-source Windows gaming and performance utility for explicit per-game CPU profiles, background-process session optimization, and selected game configuration.

![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4) ![.NET](https://img.shields.io/badge/.NET-10-512BD4) [![CI](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml) [![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

FrameHub is in active development; v0.5 is unreleased.

## What each module does

| Module | Purpose |
| --- | --- |
| Game Library | Scan Steam, Epic, custom folders, or add an executable; configure a per-game CPU profile. |
| Session Optimization | Temporarily suspend selected background applications while a selected game session is active. |
| Processes & CPU | Apply CPU Sets/Affinity and priority to a running process now. |
| Profiles & Rules | Save process settings and let the profile watcher apply them later. |
| Hardware Monitor | Opt-in local CPU/GPU/RAM telemetry; off at every launch. |
| Logs & Settings | Diagnostics, language, tray behavior, and Windows startup configuration. |

## Features

- Steam, Epic, custom-folder, and manual executable library entries.
- Per-game CPU profiles, CPU Sets with Affinity fallback, and process priority.
- Path-bound process profile identities when an executable path is available; legacy name-only profiles remain supported.
- Profile watcher, safe suspend/recovery for Session Optimization, and manual sessions.
- CS2 graphics configuration, autoexec editing, collision-safe backups, and Steam Cloud/running-game safeguards.
- One valid CS2 Steam userdata profile is selected automatically; multiple profiles require an explicit numeric userdata-ID choice before writes.
- Optional local hardware monitoring, PL/EN UI, logs, tray behavior, and Windows startup options.

## Safety and transparency

FrameHub does not use DLL injection, game-memory manipulation, anti-cheat bypasses, or a kernel driver. CPU and process changes are explicit; Session Optimization suspends and later resumes processes it recorded. CS2 changes are text-config edits with backups. Hardware sensors are opened only after you enable monitoring. Some process and startup operations can require elevation; normal use does not require always running as administrator.

## Quick start

1. Build FrameHub on Windows.
2. Scan **Game Library** and select a game.
3. Save or apply a CPU profile if desired.
4. Configure **Session Optimization** for background applications you explicitly choose.
5. Enable **Hardware Monitor** only when you want telemetry.

## Build from source

Requires Windows and the .NET 10 SDK.

```powershell
git clone https://github.com/9Erza/FrameHub.git
cd FrameHub
dotnet restore .\FrameHub.slnx
dotnet build .\FrameHub.slnx
dotnet test .\FrameHub.slnx
dotnet run --project .\FrameHub.App\FrameHub.App.csproj
```

FrameHub stores settings, library data, profiles, session recovery data, logs, and backups under `%APPDATA%\FrameHub`.

See the [User Guide](docs/USER_GUIDE.md), [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Contributing guide](CONTRIBUTING.md).

## Project

Created by [9Erza](https://github.com/9Erza). Visit [DobryPC.pl](https://dobrypc.pl) or support the project through [Buy Me a Coffee](https://buymeacoffee.com/9erza). Repository: [9Erza/FrameHub](https://github.com/9Erza/FrameHub).

## License and disclaimer

Licensed under [MIT](LICENSE). Performance results and game compatibility vary by hardware and game; test changes yourself, especially on anti-cheat protected titles.
