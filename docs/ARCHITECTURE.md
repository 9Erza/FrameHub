# Architecture

FrameHub is a WPF application split into `FrameHub.App` (views, view models, localization, runtime coordination), `FrameHub.Core` (models and Windows-facing services), and `FrameHub.Tests` (MSTest regression coverage).

Library scanners produce `LibraryItem` objects, which `LibraryService` sanitizes and persists in AppData. `ProcessScannerService` builds process snapshots; `OptimizationService` applies CPU Sets/Affinity and priority, honoring path-bound profile identity. `AppRuntimeService` owns the profile watcher and routes results to the UI.

Session Optimization detects configured candidates, records suspended processes through `SessionStateService`, then resumes them during recovery. Startup uses the existing desired-state/planner/executor/coordinator flow with registry, Task Scheduler reading, verification, and UAC helper support.

`HardwareViewModel` creates `HardwareMonitorService` only after explicit enablement and reads sensors off the UI thread. Localization is dictionary-based in `LocalizationService`, with test-enforced EN/PL key parity. Settings, profiles, library data, recovery state, logs, and CS2 backups live under `%APPDATA%\FrameHub`.

Current debt: some UI text is still supplied directly by view models rather than localization keys; new user-facing text should use the localization service.

CS2 userdata resolution works only with local folders containing a valid CS2 video config. A sole candidate is automatic; several candidates require the persisted numeric userdata ID before config paths are exposed.
