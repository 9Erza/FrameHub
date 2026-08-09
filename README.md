<div align="center">

<img src="FrameHub.App/Assets/FrameHubLogo.png" alt="FrameHub logo" width="220" />

# FrameHub

**Windows gaming and performance control without black-box tweaks.**

Per-game CPU profiles, background-process session optimization,  
CS2 configuration and local hardware monitoring in one desktop application.

[**English**](README.md) · [Polski](README.pl.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
[![CI](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-2EA44F?style=flat-square)](LICENSE)
![Release](https://img.shields.io/badge/release-v0.5.0-2EA44F?style=flat-square)

</div>

> [!NOTE]
> **Current release: v0.5.0.** FrameHub remains actively developed; see the [Changelog](CHANGELOG.md) and [Roadmap](docs/ROADMAP.md) for ongoing work.

---

## What is FrameHub?

**FrameHub** is an open-source Windows utility focused on explicit, reversible and user-controlled gaming optimization.

Instead of applying hidden system tweaks or generic "FPS boost" packs, FrameHub gives you direct control over:

- what gets changed,
- when it is applied,
- which processes are affected,
- and what should be restored afterwards.

The project currently focuses on:

- **per-game CPU and process profiles,**
- **temporary background-process optimization while gaming,**
- **safe Counter-Strike 2 configuration workflows,**
- **optional local hardware monitoring and diagnostics.**

---

## Modules

| Module | What it does |
| --- | --- |
| **Game Library** | Scan Steam, Epic and custom folders, add executables manually and configure CPU settings for a specific game. |
| **Session Optimization** | Temporarily suspend selected background applications while a configured game session is active, then restore them safely. |
| **Processes & CPU** | Inspect a running process and immediately apply CPU Sets, Processor Affinity or process priority. |
| **Profiles & Rules** | Save process settings and let the profile watcher apply them automatically when a matching executable starts. |
| **Hardware Monitor** | Opt-in local CPU, GPU and RAM telemetry. Monitoring is disabled again on every new FrameHub launch. |
| **Logs & Settings** | Diagnostics, language, tray behavior, logging and Windows startup configuration. |

---

## Features

### Game Library

- Steam library scanning.
- Epic Games library scanning.
- Custom library folders.
- Manual executable entries.
- Per-game configuration.
- Running-game detection.
- CPU profile assignment for individual games.
- Filtering of known non-game Steam support packages.

### Session Optimization

- Automatic game detection.
- Manual optimization sessions.
- Temporary suspension of selected background applications.
- Safe restoration after a session.
- Recovery state for interrupted sessions.
- Process validation designed to reduce incorrect resume operations.
- Automatic and manual session workflows.

### CPU and process control

- CPU Sets support.
- Processor Affinity fallback.
- Process priority management.
- Per-game CPU profiles.
- Saved process profiles.
- Background profile watcher.
- Path-bound profile identities when an executable path is available.
- Protection against accidentally matching unrelated same-name executables.
- Legacy name-only profiles remain supported.

### Counter-Strike 2

- CS2 graphics/config workflow.
- `autoexec.cfg` editing helper.
- Backup-before-write safety flow.
- Collision-safe backup names.
- Steam Cloud warnings.
- CS2-running safety checks.
- Multiple Steam `userdata` profiles handled safely.
- One valid userdata profile is selected automatically.
- Multiple valid candidates require explicit selection before write operations are enabled.

### Hardware monitoring

- Local CPU telemetry.
- Local GPU telemetry.
- RAM monitoring.
- Monitoring is completely opt-in.
- Sensor polling runs only after monitoring is explicitly enabled.
- Monitoring starts disabled again after every new FrameHub launch.

### Windows integration

- Polish and English UI.
- Application logs and activity history.
- Tray support.
- Minimize-to-tray and close-to-tray behavior.
- Windows startup configuration.
- Normal startup through the current user context.
- Optional elevated startup configuration when required.
- Administrator privileges are not required for normal application use.

---

## Safety and transparency

FrameHub is deliberately conservative about optimization.

FrameHub does **not**:

- inject DLLs into games,
- modify game memory,
- install a kernel driver,
- bypass anti-cheat systems,
- silently apply undocumented Windows tweaks,
- use generic "one-click FPS boost" packs,
- silently write to an ambiguous CS2 Steam account.

CPU and process changes are explicit and user-controlled.

Session Optimization records processes suspended during a session so they can be restored afterwards.

CS2 configuration changes are text-based and protected by backups and runtime safety checks.

Hardware sensors are initialized only after you explicitly enable monitoring.

> [!WARNING]
> No third-party utility can guarantee compatibility with every game, anti-cheat platform or hardware configuration.  
> Review the settings you apply and test changes on your own system.

---

## Quick start

1. Open **Game Library**.
2. Scan Steam, Epic or your custom folders, or add a game manually.
3. Select a game and configure a CPU profile if desired.
4. Configure **Session Optimization** if you want FrameHub to temporarily suspend selected background applications while gaming.
5. Use **Processes & CPU** when you want direct control over an already running process.
6. Enable **Hardware Monitor** only when you want local telemetry.

For detailed usage instructions, see the [User Guide](docs/USER_GUIDE.md).

---

## Build from source

### Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- Git

Clone the repository:

```powershell
git clone https://github.com/9Erza/FrameHub.git
cd FrameHub
```

Restore dependencies:

```powershell
dotnet restore .\FrameHub.slnx
```

Build:

```powershell
dotnet build .\FrameHub.slnx
```

Run tests:

```powershell
dotnet test .\FrameHub.slnx
```

Run FrameHub:

```powershell
dotnet run --project .\FrameHub.App\FrameHub.App.csproj
```

---

## Application data

FrameHub stores application data under:

```text
%APPDATA%\FrameHub
```

This includes:

- settings,
- game library data,
- saved profiles,
- application logs,
- Session Optimization recovery data,
- application-managed backups.

---

## Documentation

- **[User Guide](docs/USER_GUIDE.md)**  
  Detailed usage instructions.

- **[Polish User Guide](docs/USER_GUIDE.pl.md)**  
  Instrukcja użytkownika po polsku.

- **[Architecture](docs/ARCHITECTURE.md)**  
  Current architecture and major service boundaries.

- **[Roadmap](docs/ROADMAP.md)**  
  Implemented, planned and experimental work.

- **[Changelog](CHANGELOG.md)**  
  Current unreleased changes and future release history.

- **[Contributing](CONTRIBUTING.md)**  
  Development and contribution guidelines.

- **[Security Policy](SECURITY.md)**  
  Information about reporting security-sensitive issues.

---

## Project & support

### Author

[**9Erza on GitHub**](https://github.com/9Erza)

### Website

[**DobryPC.pl**](https://dobrypc.pl)

### Support development

[**☕ Buy Me a Coffee**](https://buymeacoffee.com/9erza)

### Repository

[**github.com/9Erza/FrameHub**](https://github.com/9Erza/FrameHub)

If FrameHub is useful to you, **starring the repository** or supporting development helps the project grow.

---

## License

FrameHub is licensed under the [MIT License](LICENSE).

