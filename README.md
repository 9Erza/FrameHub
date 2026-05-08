# FrameHub

FrameHub is an open-source Windows Gaming Performance Hub focused on transparent, reversible and safe performance workflows.

FrameHub is a standalone open-source project for Windows users who want clear control over games, processes, CPU assignment, background apps and future system tooling. The app focuses on transparent changes, local configuration, backups and reversible workflows.

## Current stage

**Version:** `0.3.1`  
**Status:** Library foundation + migrated optimization core

Current working foundation:

- Core Control: process priority, CPU affinity and CPU Sets
- Saved process profiles
- Background profile watcher
- Logs
- Manual hardware monitor module
- Library foundation with manual EXE adding, Steam scan, Epic scan and custom folder scan
- English / Polish localization
- Independent AppData folder: `%APPDATA%\\FrameHub`

## Product map

FrameHub is being structured around these modules:

- **Dashboard** — current status, watcher state, last activity and module map
- **Library** — game/app library with EXE path binding, Steam/Epic scanning, custom folder scanning and profile cards
- **Session Optimizer** — future transparent session workflow with restore actions
- **Core Control** — current CPU Sets, affinity, priority and process profile module
- **Profiles & Rules** — saved process rules and future game/app rules
- **Background Control** — future background app priority, CPU and memory-pressure rules
- **System Toolkit** — future safe cleanup, DNS presets, repair tools and restore points
- **Hardware** — manual hardware telemetry, disabled until enabled
- **Benchmarks** — future FPS / frametime / before-after reports
- **Windows Tuning** — preview-only future Windows tuning module with restore/backup requirements
- **Settings** — app configuration
- **Logs** — runtime diagnostics

## Safety principles

FrameHub should remain safe, transparent and open-source:

- No game memory editing
- No DLL injection
- No kernel drivers
- No anti-cheat bypass behavior or wording
- No hidden destructive tweaks
- No default disabling of Defender, Memory Integrity or anti-cheat services
- Global changes must be logged and designed for restore/rollback

## Roadmap

### v0.3.1 — CS2 UI/UX Polish

- Add game/app manually
- EXE path binding
- Steam library scanner
- Epic manifest scanner
- Custom folder locations and candidate scan
- Game/app running status
- Profile type: game, app, background app, launcher

### v0.3.0 — Session Optimizer

- Start/stop optimized sessions
- Apply selected profile
- Optional power plan switch
- Restore global changes after game exit

### v0.4.0 — Background Control

- Lower priority for selected apps
- Move selected apps to background cores
- Memory Relief with warnings and denylist

### v0.5.0 — System Toolkit

- Safe cleanup preview
- DNS presets
- Repair tools
- Restore point creation
- Classic Windows 11 context menu toggle

### v0.6.0 — Benchmarks

- FPS / frametime capture foundation
- Before/after report
- CSV export

### Later — Windows Tuning

- Preview-first Windows tuning
- Restore point and backup requirements
- Reversible actions only

## License

See `LICENSE`.
