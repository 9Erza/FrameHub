# Service catalog

Meaningful runtime authorities and extension points are cataloged below. Paths are repository-relative.

## Process and optimization

### ProcessObservationSnapshotProvider

- **NAME:** `ProcessObservationSnapshotProvider`
- **PATH / PROJECT:** `FrameHub.Core/Services/ProcessObservationSnapshotProvider.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** on-demand non-destructive full-process metadata enumeration; short-TTL/single-flight snapshot generation.
- **LIFETIME / MAJOR CONSUMERS:** one under runtime `ProcessScannerService`; Library/Background projections, benchmark discovery, Active Game, Session snapshots.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** 250 ms snapshot, generation, one in-flight task; calls `Process.GetProcesses`; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** discovery only; never authorize mutation; add projections to scanner instead of adding scanners.

### ProcessScannerService

- **NAME:** `ProcessScannerService`
- **PATH / PROJECT:** `FrameHub.Core/Services/ProcessScannerService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** user/profile/Library process projections and sole CPU delta sampling.
- **LIFETIME / MAJOR CONSUMERS:** App runtime singleton; Processes/Library/Session/Companion adapters.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** CPU-time cache; full enumeration for CPU UI, targeted name scans, shared snapshot projection; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** fresh targeted Library identities are mutation inputs; preserve start/name/path checks.

### ProcessService

- **NAME:** `ProcessService`
- **PATH / PROJECT:** `FrameHub.Core/Services/ProcessService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** priority, affinity, CPU Sets, user-process classification, current core selection.
- **LIFETIME / MAJOR CONSUMERS:** App runtime singleton; Optimization and process UI.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** no loop; Win32 process handles and CPU Set APIs.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** call only after identity validation and benchmark arbitration.

### OptimizationService

- **NAME:** `OptimizationService`
- **PATH / PROJECT:** `FrameHub.Core/Services/OptimizationService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** profile application, signature deduplication, scheduling-operation result projection.
- **LIFETIME / MAJOR CONSUMERS:** App runtime singleton; watcher and manual process/profile/Library commands.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** applied-signature cache; reacquires PID and delegates native work; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** caller must hold benchmark mutation lease; keep fresh PID/start/name/path matching.

## Session Optimization

### SessionOptimizationCoordinator

- **NAME:** `SessionOptimizationCoordinator`
- **PATH / PROJECT:** `FrameHub.App/Services/SessionOptimizationCoordinator.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** sole Session lifecycle, WAL/recovery, taskbar state, mutation gate, and shared preview/query policy.
- **LIFETIME / MAJOR CONSUMERS:** App runtime lifetime; Desktop Session VM and `AppSessionOptimizationProvider`.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** active session, gates, shutdown token; delegates process/taskbar work; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** obtains benchmark mutation lease and persists intent before mutation; do not split lifecycle state.

### ProcessSuspendService

- **NAME:** `ProcessSuspendService`
- **PATH / PROJECT:** `FrameHub.Core/Services/SessionOptimization/ProcessSuspendService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** snapshot filtering primitives and suspend/resume/resolve native operations.
- **LIFETIME / MAJOR CONSUMERS:** owned by Session coordinator; coordinator queries and lifecycle.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** no cache/timer; shared observation for snapshots; NtSuspend/NtResume plus live identity reads.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** critical/protected/anti-cheat policy and PID reuse validation must remain fail-closed.

### SessionStateService and SessionOptimizationSettingsService

- **NAME:** `SessionStateService`, `SessionOptimizationSettingsService`
- **PATH / PROJECT:** `FrameHub.Core/Services/SessionOptimization/*Service.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** recovery journal and Session preferences respectively.
- **LIFETIME / MAJOR CONSUMERS:** coordinator lifetime; Session coordinator/query path.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** file-backed JSON with atomic service; no native work or timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** preserve WAL persist-before-mutation and conservative recovery semantics.

## Benchmark and telemetry

### BenchmarkCaptureCoordinator

- **NAME:** `BenchmarkCaptureCoordinator`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/BenchmarkCaptureCoordinator.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** sole benchmark reservation/lifecycle, cancellation, terminal state, preemption, and external mutation arbitration.
- **LIFETIME / MAJOR CONSUMERS:** App runtime lifetime; Benchmark VM/provider, Session, Background Apps, profile mutations.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** locked reservation/state and external-owner count; capture task/countdown only when started.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** use this arbiter for every future environment mutation; no parallel state checks.

### BenchmarkGameDetectionService

- **NAME:** `BenchmarkGameDetectionService`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/BenchmarkGameDetectionService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** Library-game-to-running-process candidate resolution.
- **LIFETIME / MAJOR CONSUMERS:** benchmark and Active Game detectors.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** no state/timer; consumes shared observation adapter.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** candidate discovery is non-authoritative; capture revalidates exact identity.

### PresentMonApiCaptureBackend

- **NAME:** `PresentMonApiCaptureBackend`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/PresentMonApiCaptureBackend.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** benchmark PresentMon frame acquisition and raw-frame persistence.
- **LIFETIME / MAJOR CONSUMERS:** per capture; coordinator pipeline.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** PresentMon query loop during capture; disk writes.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** exact PID only; retain live-owner preemption protocol and benchmark math separation.

### LivePerformanceTelemetryService

- **NAME:** `LivePerformanceTelemetryService`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/LivePerformanceTelemetryService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** sole live PresentMon ownership and live performance snapshots.
- **LIFETIME / MAJOR CONSUMERS:** App runtime/Companion lifetime; telemetry snapshot provider.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** latest snapshot/native generation; 250 ms loop and 5 s retry; native PresentMon.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** benchmark preempts it; no new owner outside protocol.

### ActiveGameMonitor

- **NAME:** `ActiveGameMonitor`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/ActiveGameMonitor.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** live/Companion active-game snapshot and multi-game Session disambiguation.
- **LIFETIME / MAJOR CONSUMERS:** runs while Companion is active; live telemetry and App telemetry.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** latest snapshot; 2 s observation loop; Library/session disk reads.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** not benchmark lifecycle authority; discovery only.

### BenchmarkStorageService

- **NAME:** `BenchmarkStorageService`
- **PATH / PROJECT:** `FrameHub.Core/Services/Benchmarking/BenchmarkStorageService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** benchmark session directories, schema data, history/detail deletion.
- **LIFETIME / MAJOR CONSUMERS:** benchmark workflow/provider; history UI.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** disk-backed, atomic metadata writes; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** preserve schema/version and session-directory ownership.

## Library and remote control

### GamingQuickActionService

- **NAME:** `GamingQuickActionService`
- **PATH / PROJECT:** `FrameHub.App/Services/GamingQuickActionService.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** Desktop Gaming Mode quick-action orchestration policy only (gate order, launch→session sequencing, result projection).
- **LIFETIME / MAJOR CONSUMERS:** App runtime lifetime; Dashboard Gaming Mode section via `AppRuntimeService.GamingQuickActions`.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** one non-queuing action gate; no cache/timer; delegates launch, cooldown, and Session lifecycle to existing owners.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** stateless compose boundary — never a second Session/benchmark authority; already-running discovery is display-level only and never authorizes mutation.

### RiotLibraryScanner and RiotGameProcesses

- **NAME:** `RiotLibraryScanner`, `RiotGameProcesses`
- **PATH / PROJECT:** `FrameHub.Core/Services/Library/RiotLibraryScanner.cs`, `FrameHub.Core/Services/Library/RiotGameProcesses.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** passive Riot Games discovery through official Riot-created Start Menu shortcuts and the curated Riot process/protection knowledge.
- **LIFETIME / MAJOR CONSUMERS:** Library presentation scan commands; `ProcessSuspendService` and `OptimizationService` consume the protected-name set.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** none; reads Start Menu `.lnk` targets/arguments through the Windows shell on demand; no timer, no memory access, no Riot internals.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** launch uses the trusted shortcut itself via ShellExecute with no FrameHub arguments; Riot game/client/Vanguard processes are never mutated and never benchmark targets; do not add Riot metadata, LCU, or network discovery here.

### LibraryService

- **NAME:** `LibraryService`
- **PATH / PROJECT:** `FrameHub.Core/Services/Library/LibraryService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** sanitize, merge, load, and persist Library items.
- **LIFETIME / MAJOR CONSUMERS:** service instances in Library presentation and App providers/coordinator query.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** file-backed atomic JSON; no timer/native work.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** remote providers reload server-side items; preserve sanitization.

### AppLibraryProvider

- **NAME:** `AppLibraryProvider`
- **PATH / PROJECT:** `FrameHub.App/Services/AppLibraryProvider.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** regular Companion Library listing and trusted launch orchestration.
- **LIFETIME / MAJOR CONSUMERS:** App runtime lifetime; Companion Library controller.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** non-queuing launch gate; shared cooldown service; on-demand batch observation.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** opaque IDs and safe DTOs; reload and filter server-side items.

### AppBackgroundAppProvider

- **NAME:** `AppBackgroundAppProvider`
- **PATH / PROJECT:** `FrameHub.App/Services/AppBackgroundAppProvider.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** Background App listing and Start/Stop orchestration.
- **LIFETIME / MAJOR CONSUMERS:** App runtime lifetime; Companion Background Apps controller.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** non-queuing operation gate; one batch observation per list; delegates launch/stop.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** explicit opt-in/scopes, benchmark lease, fresh Stop identities, safe DTOs.

### AppLibraryControlService

- **NAME:** `AppLibraryControlService`
- **PATH / PROJECT:** `FrameHub.App/Services/AppLibraryControlService.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** trusted Background App Start/Stop primitive.
- **LIFETIME / MAJOR CONSUMERS:** Background provider lifetime; `AppBackgroundAppProvider`.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** no cache/timer; fresh targeted Stop scan and close/kill terminator.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** terminator revalidates PID/start/name/path immediately before close/kill.

### LibraryLaunchReservationService

- **NAME:** `LibraryLaunchReservationService`
- **PATH / PROJECT:** `FrameHub.App/Services/LibraryLaunchReservationService.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** one shared three-second, per-item post-launch cooldown.
- **LIFETIME / MAJOR CONSUMERS:** one instance per App runtime; `AppLibraryProvider` and `AppBackgroundAppProvider` receive that same instance.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** locked item/timestamp dictionary; no native work and no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** same-item launches are rejected during cooldown while different items remain independent; never duplicate the runtime owner.

## Runtime, hardware, Companion, persistence

### AppRuntimeService

- **NAME:** `AppRuntimeService`
- **PATH / PROJECT:** `FrameHub.App/Services/AppRuntimeService.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** application lifetime composition, profile watcher, shared services, Companion synchronization, hardware leases.
- **LIFETIME / MAJOR CONSUMERS:** one per WPF shell; all major view models and App providers.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** settings/profiles/activity/hardware lease count; configurable profile watcher.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** compose shared authorities here; all profile mutation passes benchmark arbitration.

### HardwareMonitorService

- **NAME:** `HardwareMonitorService`
- **PATH / PROJECT:** `FrameHub.Core/Services/HardwareMonitorService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** LibreHardwareMonitor sensor backend and metrics projection.
- **LIFETIME / MAJOR CONSUMERS:** one private App runtime instance; Hardware VM and Companion snapshots via leases.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** open sensor computer while leased; synchronous hardware updates; App runtime shares a 200 ms timestamped result; no own timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** reuse this backend and runtime cache; do not add another sensor owner.

### AppTelemetrySnapshotProvider

- **NAME:** `AppTelemetrySnapshotProvider`
- **PATH / PROJECT:** `FrameHub.App/Services/AppTelemetrySnapshotProvider.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** composed Companion telemetry snapshot.
- **LIFETIME / MAJOR CONSUMERS:** Companion-enabled runtime; REST and WebSocket providers.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** latest immutable DTO; 500 ms publication loop; conditional hardware read.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** presentation snapshot only; do not move business authority into DTO assembly.

### CompanionServer and authentication services

- **NAME:** `CompanionServer`, `CompanionAuthMiddleware`, `PairingEngine`, `CompanionScopes`
- **PATH / PROJECT:** `FrameHub.Companion/*` / Companion
- **RESPONSIBILITY / AUTHORITATIVE FOR:** host lifecycle, transport routing, credential verification, pairing, and scope policy.
- **LIFETIME / MAJOR CONSUMERS:** App runtime server lifetime; controllers, WebSocket handler, browser frontend.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** Kestrel state, rate/ticket stores; network work; no process mutation.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** explicit scopes and loopback/LAN policy; controllers delegate backend rules.

### DeviceRecordStore

- **NAME:** `DeviceRecordStore`
- **PATH / PROJECT:** `FrameHub.Companion/Persistence/DeviceRecordStore.cs` / Companion
- **RESPONSIBILITY / AUTHORITATIVE FOR:** paired-device records and protected credential persistence.
- **LIFETIME / MAJOR CONSUMERS:** Companion server/pairing lifetime.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** locked in-memory list, fault state, temp-plus-overwrite disk write; no timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** persist-before-publish; deliberate `AtomicFileService` exception; never store plaintext credentials.

### LocalizationService

- **NAME:** `LocalizationService`
- **PATH / PROJECT:** `FrameHub.App/Services/LocalizationService.cs` / App
- **RESPONSIBILITY / AUTHORITATIVE FOR:** Desktop EN/PL dictionaries and language change notification.
- **LIFETIME / MAJOR CONSUMERS:** shell lifetime; WPF view models.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** current language/dictionaries; no native work/timer.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** preserve key parity; large cohesive dictionaries are intentionally retained.

### Cs2OptimizationService and GameDataService

- **NAME:** `Cs2OptimizationService`, `GameDataService`
- **PATH / PROJECT:** `FrameHub.Core/Services/GameOptimization/Cs2OptimizationService.cs`, `FrameHub.Core/Services/GameData/GameDataService.cs` / Core
- **RESPONSIBILITY / AUTHORITATIVE FOR:** production CS2 config/backup operations; dormant declarative data loading respectively.
- **LIFETIME / MAJOR CONSUMERS:** CS2 service owned by Library presentation; GameData has no production consumer.
- **STATE/CACHE / OS/NATIVE WORK / BACKGROUND WORK:** filesystem/config work; no own timers.
- **TRUST/SECURITY ROLE / NOTES FOR EXTENSION:** do not create a third game-integration architecture; decide GameData adoption/removal separately.
