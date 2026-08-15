# Feature map

| I want to… | Use or check first |
|---|---|
| Observe all processes for display/discovery | `ProcessObservationSnapshotProvider`, then a projection in `ProcessScannerService` |
| Show process CPU/memory | `ProcessScannerService.ScanUserProcessesAsync`; do not add a second CPU sampler |
| Batch Library running badges | `ProcessScannerService.FindRunningLibraryItemIdsAsync` |
| Revalidate a Background App before Stop | `FindRunningLibraryItemProcessesAsync`, `AppLibraryControlService`, `SystemTrustedProcessTerminator` |
| Change priority, affinity, or CPU Sets | `OptimizationService` + `ProcessService`, protected by benchmark mutation arbitration |
| Add or apply a process profile | `ProfileService`, `AppRuntimeService`, `OptimizationService` |
| Change automatic profile watching | `AppRuntimeService.RunProfileWatcherOnceAsync` and `BACKGROUND-WORK.md` |
| Change Session lifecycle/recovery | `SessionOptimizationCoordinator`, `SessionStateService`; preserve WAL semantics |
| Change Session preview/rules | Coordinator query methods, `BackgroundProcessRuleFactory`, snapshot overloads in `ProcessSuspendService` |
| Change suspend/resume native behavior | `ProcessSuspendService`; preserve fresh identity validation |
| Change benchmark lifecycle | `BenchmarkCaptureCoordinator` only |
| Detect benchmark targets/games | `BenchmarkGameDetectionService`, `BenchmarkGameResolver`, shared observation provider |
| Change benchmark statistics | `BenchmarkAnalyzer`, `BenchmarkStatistics`, quality evaluator, and dedicated tests |
| Change benchmark storage/history | `BenchmarkStorageService` and schema-aware models |
| Change benchmark PresentMon capture | `PresentMonApiCaptureBackend` and existing preemption protocol |
| Change live PresentMon | `LivePerformanceTelemetryService`; do not add another owner |
| Change hardware telemetry | `HardwareMonitorService`, App runtime leases, `AppTelemetrySnapshotProvider` |
| Scan or persist Library items | scanner services (Steam, Epic, Riot shortcuts, custom folders) and `LibraryService` |
| Launch a Desktop Library item | `AppLibraryLaunchService` and `LibraryViewModel` |
| Start a Desktop Gaming Mode quick action | `GamingQuickActionService` via `AppRuntimeService.GamingQuickActions`; Dashboard presents, coordinator owns lifecycle |
| List/launch remote regular Library items | `AppLibraryProvider` |
| List/control remote Background Apps | `AppBackgroundAppProvider`, `AppLibraryControlService`, shared launch reservations |
| Add a Companion route | controller + provider contract; backend rules stay in Core/App |
| Change Companion authentication/scopes | `CompanionAuthMiddleware`, `PairingEngine`, `CompanionScopes`, `DeviceRecordStore` |
| Add a remote mutation | opaque server ID, explicit write scope, server reload, benchmark arbitration, fresh identity |
| Change WebSocket telemetry | `TelemetryWebSocketHandler` and `auth-transport.js` |
| Change Companion dashboard rendering | `telemetry.js` |
| Change Companion benchmark UI | `benchmarks.js` |
| Change Companion Library/Background UI | `library.js` |
| Change Companion Session UI | `session-optimization.js` |
| Change frontend bootstrap/navigation | `app.js`; do not move domain logic back into it |
| Add localization text | Desktop `LocalizationService` or Companion `i18n.js`; keep EN/PL parity |
| Persist general JSON safely | existing domain owner and `AtomicFileService` where semantics match |
| Change paired-device persistence | `DeviceRecordStore`; preserve persist-before-publish/faulted state |
| Change CS2 integration | `Cs2OptimizationService` plus existing Library presentation |
| Explore declarative game integrations | inspect dormant `FrameHub.GameData`; do not create a third architecture |
| Add or change recurring work | existing authority plus an update to `BACKGROUND-WORK.md` |
