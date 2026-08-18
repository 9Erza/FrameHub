<div align="center">

<img src="FrameHub.App/Assets/FrameHubLogo.png" alt="FrameHub logo" width="220" />

# FrameHub

**Windows gaming and performance control without black-box tweaks.**

Game library, per-game CPU profiles, session optimization, local frame-time benchmarking,
LAN Companion server, CS2 configuration and hardware monitoring in one desktop application.

[**English**](README.md) · [Polski](README.pl.md)

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat-square)
[![CI](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml/badge.svg)](https://github.com/9Erza/FrameHub/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-2EA44F?style=flat-square)](LICENSE)
![Release](https://img.shields.io/badge/release-v0.7.0-2EA44F?style=flat-square)

</div>

> [!NOTE]
> **Current release: v0.7.0.** See the [Changelog](CHANGELOG.md) for release details and the [Roadmap](docs/ROADMAP.md) for future work.

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
- **active-game CPU scheduling control (CPU Sets & Affinity),**
- **LAN Companion server for mobile device monitoring and control,**
- **local per-frame benchmarking, history and same-game comparison with environment metadata,**
- **safe Counter-Strike 2 configuration workflows,**
- **optional local hardware monitoring and diagnostics.**

---

## Modules

| Module | What it does |
| --- | --- |
| **Games & Optimization** | Scan Steam, Epic and custom folders, add executables manually, launch games, and configure CPU optimization settings for specific games. |
| **Session Optimization** | Temporarily suspend selected background applications while a configured game session is active, then restore them safely. |
| **Game CPU Assignment** | Temporarily assign active-game CPU cores (Affinity / CPU Sets) with topology presets (All, Physical Only, Clear). |
| **Companion Server** | Local LAN web server enabling real-time mobile hardware telemetry, benchmark control, and library launching with secure pairing. |
| **Benchmarks** | Detect running library games, capture exact-process frame timing with environment metadata, graph frame times, retain local history and compare same-game sessions. |
| **Processes & CPU** | Inspect a running process and immediately apply CPU Sets, Processor Affinity or process priority. |
| **Profiles & Rules** | Save process settings and let the profile watcher apply them automatically when a matching executable starts. |
| **Hardware Monitor** | Opt-in local CPU, GPU and RAM telemetry with lease-controlled sensor lifecycle. |
| **Logs & Settings** | Diagnostics, language, tray behavior, logging, Companion pairing, and Windows startup configuration. |

### Benchmarking in v0.7.0

Start a game from Game Library, open **Benchmarks** (or use the game's **Benchmark** action), choose a duration, and reproduce the same scene each time. FrameHub shows Average FPS, median, 1% Low, 0.1% Low, P95/P99 frame time, environment metadata context (OS, CPU, GPU driver, RAM, display), quality diagnostics, a spike-preserving frame-time graph, local history, and same-game comparisons. Raw frames and summaries remain on this machine under `%LOCALAPPDATA%\FrameHub\Benchmarks`; FrameHub adds no upload, analytics, account, or cloud service.

Benchmark capture uses Intel PresentMon Shared Service/API. The official pinned PresentMon v2.5.1 MSI is embedded in the single FrameHub Setup, so users do not download a second installer. PresentMon is a shared MIT-licensed prerequisite and may remain installed after FrameHub is removed; see [Third-party notices](docs/THIRD-PARTY-NOTICES.md).

FrameHub itself does not inject DLLs into games, read or modify game memory, install a FrameHub kernel driver, or bypass anti-cheat. It uses PresentMon's documented service/API/ETW path. Game and anti-cheat compatibility can vary, so compatibility with every title is not guaranteed.

---

## Features

### Game Library & Quick Actions

- Steam library scanning.
- Epic Games library scanning.
- Custom library folders.
- Manual executable entries.
- Per-game configuration.
- Running-game detection.
- CPU profile assignment for individual games.
- Gaming Quick Actions: one-click launch combining game startup and Session Optimization.
- Conservative Riot Games management (League of Legends, VALORANT) with passive shortcut discovery and launch.
- Missing executable safeguards and status indicators.
- Filtering of known non-game Steam support packages.

### Session Optimization & Game CPU Assignment

- Automatic game detection.
- Manual optimization sessions.
- Temporary suspension of selected background applications.
- Active-game CPU Sets and Affinity temporary overrides.
- Core topology presets (All Cores, Physical Only, Clear).
- Safe restoration after a session.
- Recovery state for interrupted sessions.
- Process validation designed to reduce incorrect resume operations.

### LAN Companion Server & Mobile Web UI

- Lightweight ASP.NET Core Kestrel LAN web server.
- Cryptographically secure pairing with opaque device records and QR code connection.
- Granular permission scopes for read/write access.
- Real-time WebSocket hardware telemetry streaming.
- Mobile game library browsing and remote launching.
- Mobile benchmark status and capture controls.
- Mobile Session Optimization and Game CPU Assignment cards.
- Client-side game icon caching via IndexedDB.

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
- Sensor polling runs only while an active consumer lease exists and monitoring is enabled.
- Automatic sensor shutdown on consumer release.

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
- mutate or benchmark Riot game/client/Vanguard processes,
- silently write to an ambiguous CS2 Steam account.

CPU and process changes are explicit and user-controlled.

Session Optimization records processes suspended during a session so they can be restored afterwards.

CS2 configuration changes are text-based and protected by backups and runtime safety checks.

Hardware sensors are initialized only after you explicitly enable monitoring.

> [!WARNING]
> No third-party utility can guarantee compatibility with every game, anti-cheat platform or hardware configuration.  
> Review the settings you apply and test changes on your own system.

---

## Development, support and compatibility

### Development transparency

FrameHub is an independent hobby project developed and maintained by a single author in spare time. Because it is not backed by a company or a dedicated engineering team, available development time is naturally limited.

Development is structured around automated testing, focused code review and conservative technical decisions. Modern tooling, including AI-assisted development and research tools, is used throughout the workflow to assist with implementation, testing, targeted code reviews, documentation and technical research. Architectural direction, feature scope, safety boundaries and release decisions remain entirely maintainer-directed.

### Support, warranty and anti-cheat compatibility

- **Maintenance and issue reports**: Bug reports and security-sensitive disclosures are welcome via GitHub issues and private maintainer contact (see [Security Policy](SECURITY.md)). The maintainer intends to investigate and address meaningful bugs and security issues as time permits, but no response times, SLAs, fix deadlines or continuous release schedules can be guaranteed.
- **License and warranty**: FrameHub is distributed under the terms of the [MIT License](LICENSE) on an "AS IS" basis, without warranties of any kind. The [LICENSE](LICENSE) file contains the authoritative legal terms.
- **Anti-cheat philosophy and non-invasiveness**: FrameHub is designed with a deliberately conservative approach toward gaming environments and anti-cheat platforms. The project actively avoids invasive mechanisms, including DLL injection, reading or modifying game memory, kernel-mode drivers, debugger attachment, anti-cheat circumvention, undocumented game hooks and intentional security bypasses.
- **Research and risk assessment**: Potential features interacting with games, system processes or telemetry are evaluated against official documentation, primary technical sources and manual research, supplemented by cross-checking across independent AI-assisted research tools. If a proposed approach introduces meaningful uncertainty, unnecessary invasiveness or an unclear anti-cheat interaction, the design choice is to reject or omit the feature rather than accept unnecessary risk.
- **Handling newly identified risks**: If credible evidence indicates that an existing feature introduces an unexpected compatibility or security risk, the maintainer's policy is to restrict, disable or remove the functionality until it can be safely addressed.
- **No formal vendor certification**: FrameHub is an independent hobby project and has no formal partnerships, endorsements or certifications from game publishers or anti-cheat vendors. Because games, operating system updates, graphics drivers and anti-cheat heuristics evolve continuously—and many anti-cheat internals are intentionally undocumented—no third-party utility can promise permanent or complete compatibility. Users should review configured settings and test them on their own systems.

---

## Quick start

1. Open **Game Library**.
2. Scan Steam, Epic or your custom folders, or add a game manually.
3. Select a game and configure a CPU profile or use **Quick Start** on the Dashboard.
4. Configure **Session Optimization** if you want FrameHub to temporarily suspend selected background applications while gaming.
5. Use **Processes & CPU** when you want direct control over an already running process.
6. Pair your mobile device under **Settings > Companion** for remote LAN monitoring and control.
7. Enable **Hardware Monitor** only when you want local telemetry.

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

### Build the installer

Install Inno Setup 6, then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

The build prepares the official Intel PresentMon v2.5.1 MSI automatically, verifies its pinned SHA-256, embeds it in the generated FrameHub Setup, and keeps the prerequisite cache under the gitignored `artifacts\prerequisites\PresentMon` directory.

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
- companion device pairings,
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
  Current release notes and release history.

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
