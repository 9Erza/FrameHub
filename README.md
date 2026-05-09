<h1 align="center">FrameHub</h1>

<p align="center">
  Windows performance and game optimization tool with CPU profile management, process optimization, and dedicated Counter-Strike 2 configuration support.
</p>

<p align="center">
  <img src="FrameHub.App/Assets/FrameHubLogo.png" alt="FrameHub Logo" width="220" />
</p>

<p align="center">
  <img src="https://img.shields.io/badge/license-MIT-97CA00?style=for-the-badge" alt="License MIT" />
  <img src="https://img.shields.io/badge/release-v0.3.1--beta-1E90FF?style=for-the-badge" alt="Release v0.3.1-beta" />
  <img src="https://img.shields.io/badge/platform-Windows-0078D6?style=for-the-badge" alt="Platform Windows" />
  <img src="https://img.shields.io/badge/status-Beta-orange?style=for-the-badge" alt="Status Beta" />
</p>

**FrameHub** is an open-source Windows Gaming Performance Hub built for transparent, reversible and user-controlled game optimization.

The app is currently focused on **Counter-Strike 2** and CPU/process optimization, but the long-term goal is to become an all-in-one performance hub for games, background apps, profiles, benchmarks and safe Windows tuning.

> Current version: **v0.3.1 Beta**  
> Platform: **Windows 10 / Windows 11**  
> Status: **public beta / early release**

---

## What FrameHub does

FrameHub is designed to help users manage game performance without hidden tweaks, unsafe system changes or black-box behavior.

Current core features:

- Game and application library
- Manual `.exe` binding
- Steam library scanning
- Epic Games manifest scanning
- Custom folder scanning
- Running process detection
- CPU Sets and CPU affinity management
- Process priority management
- Saved optimization profiles
- Background profile watcher
- CS2 graphics configuration editor
- CS2 crosshair editor
- CS2 `autoexec.cfg` helper
- Backup and restore flow for CS2 config files
- Polish and English UI
- Local logs and user settings stored in `%APPDATA%\FrameHub`

Planned modules are visible in the app as roadmap sections, but not every module is active yet.

---

## v0.3.1 Beta focus

This beta is mainly about three areas:

1. **Library and profiles**  
   Add games/apps, bind executables, create CPU optimization profiles and let FrameHub monitor processes in the background.

2. **Core Control**  
   Inspect running processes, read current CPU assignment and apply CPU Sets / Affinity / Priority settings manually or through saved profiles.

3. **Counter-Strike 2 Config**  
   Edit selected CS2 video/config values, crosshair settings and safe `autoexec.cfg` entries while the game is closed.

---

## CPU optimization logic

FrameHub separates two different actions:

### Neutral profile editor

When a game has **no saved optimization profile**, the editor starts in a neutral state:

- Mode: `CPU Sets`
- Priority: `Normal`
- All logical processors selected

Nothing is changed until the user saves a profile or clicks **Optimize**.

### Optimize button

The **Optimize** button creates or overwrites the current CPU profile for the selected game using a standard preset.

The preset always uses:

- Mode: `CPU Sets`
- Priority: `Normal`

CPU selection depends on the detected CPU layout:

| CPU type | Standard FrameHub preset |
|---|---|
| AMD with SMT | Disable SMT threads and disable primary Core 0 |
| Legacy Intel without P/E-core split | Disable Core 0 and disable HT threads when available |
| Intel with P-cores, E-cores and HT | Disable E-cores and disable HT threads |
| Intel with P-cores and E-cores but no HT | Disable E-cores |
| Unknown topology | Keep all logical processors selected |

This is a conservative default. Users can edit every saved profile manually.

---

## Core Control

The **Core Control** module allows manual process tuning.

It can:

- List running processes
- Show process PID, instance count, mode, priority, CPU usage and RAM usage
- Read the real current CPU assignment when possible
- Prefer CPU Sets when available
- Fall back to classic Processor Affinity
- Apply settings immediately
- Save settings as reusable process profiles

The CPU selector is split into:

- physical cores
- SMT / Hyper-Threading threads

---

## Profiles and Rules

The **Profiles and Rules** module manages saved optimization profiles.

Users can:

- View saved profiles
- Enable or disable background monitoring for a profile
- Edit mode, priority and CPU selection
- Apply a profile immediately
- Delete a profile

The background monitor can reapply saved profiles when the target process starts again.

---

## Counter-Strike 2 support

FrameHub v0.3.1 Beta includes a dedicated CS2 configuration page named **Config**.

Current CS2 features:

- CS2 config path detection
- CS2 video settings editor
- Competitive graphics preset
- Config backup before changes
- Latest backup comparison
- Custom user-selected values
- Crosshair editor with live preview
- `autoexec.cfg` helper
- Common safe binds and commands
- Steam Cloud warning
- CS2 running detection

### CS2 safety rule

FrameHub does **not** read or write CS2 config files while `cs2.exe` is running.

When CS2 is running, Config-related actions are blocked. The user must close the game before reading or editing CS2 configuration files.

### Competitive preset

The CS2 competitive preset focuses on performance and low input latency.

It does **not** force resolution or display mode, because those are treated as user preferences. FrameHub may still show recommended values, but they are not applied automatically by the preset.

---

## Anti-cheat / VAC / FACEIT note

FrameHub is designed to avoid behavior associated with cheats.

FrameHub does **not**:

- inject DLLs
- hook the game
- edit game memory
- install kernel drivers
- bypass anti-cheat systems
- modify CS2 executable or binary game files
- remove textures, smoke, models or other gameplay assets

For CS2, FrameHub edits standard text-based configuration values while the game is closed, similar to manual config editing.

No third-party tool can honestly guarantee that every anti-cheat platform will always consider every action safe. FrameHub is therefore built around conservative rules: no runtime config access while CS2 is running, no memory manipulation and no anti-cheat bypass behavior.

---

## Installation

For public beta releases, use the installer or portable package from the GitHub Releases page.

Recommended first-run steps:

1. Close CS2 before using the CS2 Config module.
2. Run FrameHub normally.
3. Run as administrator only when Windows requires elevated permissions for process optimization.
4. Add or scan games in the Library.
5. Create or apply CPU profiles.
6. Use Config only when CS2 is closed.

---

## Build from source

Requirements:

- Windows 10 or Windows 11
- Visual Studio 2026 or newer, or compatible .NET SDK tooling
- .NET Desktop Runtime / SDK matching the project target
- WPF workload enabled

Project target:

```txt
net10.0-windows
```

Build steps:

```powershell
git clone <repo-url>
cd FrameHub
# optional cleanup if switching between local builds
.\clean_framehub_build_cache.bat
# then open FrameHub.slnx in Visual Studio and rebuild
```

Main solution file:

```txt
FrameHub.slnx
```

---

## Project structure

```txt
FrameHub.App
  WPF application, UI, view models, localization and app shell.

FrameHub.Core
  Core logic: process control, CPU topology, profiles, settings, logs,
  library scanning and CS2 config editing.

FrameHub.GameData
  Game-specific data files, presets and setting definitions.
```

Important paths:

```txt
%APPDATA%\FrameHub\settings.json
%APPDATA%\FrameHub\profiles.json
%APPDATA%\FrameHub\library.json
%APPDATA%\FrameHub\FrameHub.log
%APPDATA%\FrameHub\Backups\CS2\
```

---

## Current limitations

This is a beta release.

Known limitations:

- CS2 is currently the only game with a dedicated config editor.
- Some roadmap modules are placeholders or preview-only.
- Hardware telemetry is optional and may vary by sensor support.
- Optimization results can vary by CPU, Windows scheduler behavior, drivers and game engine.
- The app is not a replacement for proper in-game testing, frametime measurement and user-specific tuning.

---

## Roadmap

Planned directions:

- More game-specific config modules
- Safer session optimizer workflow
- Background app rules
- Benchmark and before/after reports
- Better import/export of profiles
- More detailed CPU topology explanations
- Windows tuning only with backup, preview and restore flow

---

## License

FrameHub is released under the MIT License.

See [`LICENSE`](LICENSE).
