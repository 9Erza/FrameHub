# FrameHub v0.3.1 Beta — Release Description

## Title

FrameHub v0.3.1 Beta — CS2 Config, CPU Profiles and Core Control

## Short description

FrameHub v0.3.1 Beta is the first public beta release focused on Counter-Strike 2 configuration, CPU Sets / Affinity profiles, process priority control and a safer, transparent workflow for game optimization on Windows.

---

## Release notes

This is the first beta build of **FrameHub**, an open-source Windows Gaming Performance Hub.

The goal of FrameHub is to provide a clean and transparent way to manage game profiles, process CPU assignment, priority settings and selected game configuration files without hidden tweaks or unsafe behavior.

This release is focused mainly on **Counter-Strike 2** and the foundation for future game/app optimization profiles.

---

## Highlights

### Counter-Strike 2 Config

- Dedicated CS2 **Config** page
- CS2 video setting detection
- CS2 competitive preset
- Custom user-selected CS2 setting values
- Backup creation before applying changes
- Latest backup comparison
- CS2 crosshair editor with live preview
- `autoexec.cfg` helper
- Safe bind helpers for common CS2 commands
- Steam Cloud warning for CS2 configuration changes
- Config read/write blocked while `cs2.exe` is running

### CPU and Process Optimization

- CPU Sets support
- Classic CPU Affinity support
- Process priority management
- Saved process/game profiles
- Background profile monitor
- Manual process control in **Core Control**
- Editable profiles in **Profiles and Rules**

### Library

- Manual `.exe` adding
- Steam library scanning
- Epic Games manifest scanning
- Custom folder scanning
- Running status detection for games/apps
- Linked CPU profile creation per library item

### Safety and transparency

FrameHub does not inject into games, does not hook game processes, does not edit game memory and does not install kernel drivers.

CS2 config files are only read or written while CS2 is closed. If CS2 is running, FrameHub blocks config-related actions.

---

## Important CS2 behavior

The **Optimize** button for CS2 applies two things:

1. CPU optimization profile
2. CS2 competitive graphics preset

If CS2 is running, FrameHub still handles CPU profile logic, but it does **not** read or write CS2 config files. In that case, the user gets a visible warning that graphics settings were not optimized because CS2 was running.

The CS2 competitive preset does **not** force resolution or display mode. These remain user preferences.

---

## CPU preset logic

The default Optimize action always uses:

- Mode: `CPU Sets`
- Priority: `Normal`

CPU selection depends on detected CPU topology:

| CPU type | FrameHub Optimize behavior |
|---|---|
| AMD with SMT | Disable SMT threads and primary Core 0 |
| Legacy Intel without P/E-core split | Disable Core 0 and HT threads if available |
| Intel with P-cores, E-cores and HT | Disable E-cores and HT threads |
| Intel with P-cores and E-cores but no HT | Disable E-cores |
| Unknown CPU topology | Keep all logical processors selected |

Users can edit all saved profiles manually.

---

## Known limitations

This is a beta release.

- CS2 is currently the only game with a dedicated config editor.
- Some app modules are visible as planned/preview modules and are not fully implemented yet.
- Hardware telemetry depends on sensor support.
- Process and CPU optimization results can vary by system, CPU topology, drivers and Windows scheduler behavior.
- This build is intended for testing and feedback before a wider stable release.

---

## Installation notes

Recommended:

1. Close CS2 before using the CS2 Config page.
2. Install or unpack FrameHub.
3. Start FrameHub.
4. Add or scan games in the Library.
5. Use Optimize or manually create a CPU profile.
6. For CS2 config changes, keep CS2 closed and let FrameHub create a backup before applying changes.

If process optimization fails, restart FrameHub as administrator.

---

## Anti-cheat note

FrameHub is designed to avoid cheat-like behavior.

FrameHub does **not**:

- inject DLLs
- hook CS2
- edit CS2 memory
- install anti-cheat bypass drivers
- modify CS2 executable or binary game files
- remove textures, smoke, models or gameplay assets

For CS2, FrameHub edits standard configuration files while the game is closed. No external tool can guarantee future anti-cheat decisions, but this release is intentionally designed around conservative, transparent and reversible behavior.

---

## Suggested release assets

Attach these files to the GitHub release when ready:

- `FrameHub_Setup_v0.3.1.exe`
- `FrameHub_Portable_v0.3.1.zip`
- source code archive
- optional SHA256 checksums

---

## Changelog summary

- Added CS2 configuration workflow
- Added CS2 competitive preset
- Added CS2 crosshair editor
- Added `autoexec.cfg` helper
- Added CS2 config backup workflow
- Added CPU Sets / Affinity profile workflow
- Added background profile watcher
- Added editable Profiles and Rules view
- Improved Core Control process list and CPU selector
- Improved Library game/app profile workflow
- Added Polish and English localization foundation
