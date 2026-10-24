# FrameHub NOC functional inventory (point-in-time, 2026-10-24)

Status: point-in-time inventory, not a second source of truth.
Canonical owners remain `docs/architecture/FEATURE-MAP.md` (routing),
`docs/architecture/SERVICE-CATALOG.md` (ownership),
`docs/architecture/BACKGROUND-WORK.md` (timers) and `docs/ROADMAP.md` (plans).

- Baseline: `main` @ `0c6efc24` (= BASE), v0.7.1.
- Head: `noc/t_2e7bd27e-0c6efc2` @ `2bd22cb` (3 local commits, no push).
- Classification: VERIFIED = inspected in code; REPORTED = teammate finding taken as input
  without re-audit; test-execution numbers are REPORTED from QA reports
  (`QA_REVIEW.md`, `BASELINE-REPORT.md`), not re-run here.
- QA execution (REPORTED): Desktop **655/655** (was 651 at BASE, +4 new tests),
  Companion **140/140**, Release build 0 warnings / 0 errors, `node --check` 7/7 JS.

## 1. User functions (12)

| # | Function | Location | Consumers / UI | Test coverage |
|---|---|---|---|---|
| U-1 | Game library scanning (Steam, Epic, Riot shortcuts, custom folders) | `FrameHub.Core/Services/Library/*`, `RiotLibraryScanner.cs:165-186` | `LibraryView`, Companion `library.js` | `LibraryRuntime*Tests`, `Riot*Tests` (REPORTED green) |
| U-2 | Desktop library launch (incl. cooldown) | `AppLibraryLaunchService` | `LibraryViewModel`, Gaming Mode | `AppLibrary*Tests` (REPORTED green) |
| U-3 | Gaming Quick Actions / Gaming Mode (M10.2) | `GamingQuickActionService` via `AppRuntimeService.GamingQuickActions` | Dashboard, `SessionStateService` lifecycle | `GamingQuickActionServiceTests` incl. 2 new Dashboard subscription tests (REPORTED 655/655) |
| U-4 | Session Optimization (background suspend/recovery, WAL-style atomic state) | `SessionOptimizationCoordinator` (sole lifecycle owner) | `SessionOptimizationViewModel` (3 s auto timer), Companion `session-optimization.js` | `SessionOptimizationCoordinatorTests`, `CpuControlTests`, `SessionStateServiceTests` (12) |
| U-5 | Game CPU Assignment (Affinity / CPU Sets, active game) | `ProcessService` (sole native scheduling owner) + `OptimizationService` | Desktop card + Companion card, scopes `read:optimization-cpu` / `write:optimization-cpu` | `SessionCpuApiTests`, `OptimizationRetryTests`, `PowerPlanTests` |
| U-6 | Processes & CPU view | `ProcessScannerService` (sole CPU delta sampler), `ProcessObservationSnapshotProvider` (250 ms TTL, no timer) | `ProcessesViewModel` (page-scoped refresh) | `ProcessScanner*Tests` (REPORTED green) |
| U-7 | CPU profiles + profile watcher | `ProfileService`, `AppRuntimeService.cs:29,86-90` (1–30 s, default 2 s) | Settings / profiles UI | Profile-related tests (REPORTED green) |
| U-8 | CS2 configuration workflow | `Cs2OptimizationService` + `LibraryViewModel` (~1700 lines, P1 refactor candidate — untouched) | Library item detail | `Cs2ConfigWorkflowTests` |
| U-9 | Hardware monitoring (opt-in, lease-controlled) | `HardwareMonitorService` (sole sensor backend) | `HardwareViewModel` (lease-backed refresh) | `HardwareMonitorRuntimeStateTests`, `SensorReaderTests` |
| U-10 | Benchmarks: Capture / History / Compare (PresentMon Service/API only) | `BenchmarkCaptureCoordinator` (sole lifecycle), `PresentMonApiCaptureBackend`, `BenchmarkAnalyzer`, `BenchmarkStorageService` | `BenchmarkViewModel` + `BenchmarkView.xaml`, Companion `benchmarks.js` | `BenchmarkUiCoreTests` incl. 2 new regression tests, `BenchmarkCaptureCoordinatorTests`, `PresentMon*Tests` (REPORTED green) |
| U-11 | Update check (HTTPS version.txt, 5 s timeout, silent fail; P2: no signature verification — threat-model note, not a blocker) | `UpdateService.cs` (57 lines) | Update dialog (automatic + manual) | `UpdateNotificationTests` |
| U-12 | Tray behavior, startup config, i18n (EN/PL parity), logs | `MainWindow.xaml.cs:265` (`CloseToTray`), `LocalizationService` | Tray icon, Settings, log files | `TrayAndShutdownTests` (6), `StabilizationTests` (21) |

## 2. Internal functions (delegating only — no domain duplication)

- Observation: `ProcessObservationSnapshotProvider`; projections/CPU sampler: `ProcessScannerService`;
  active-game detection: `ActiveGameMonitor` (+ `BenchmarkGameDetectionService`/`BenchmarkGameResolver` for benchmarks).
- Live PresentMon: `LivePerformanceTelemetryService` (sole owner, preempted during capture).
- Benchmark chain: `PresentMonApiFrameSource` → `PresentMonApiCaptureBackend` (identity check `:26,34`)
  → `BenchmarkAnalyzer`/`BenchmarkStatistics` → `BenchmarkStorageService` (`%LOCALAPPDATA%\FrameHub\Benchmarks`),
  env context via `IBenchmarkEnvironmentProvider` (v1, advisory only).
- Process mutation: `ProcessService`, `ProcessSuspendService` (suspend/resume + fresh-identity revalidation),
  `SystemTrustedProcessTerminator`; Companion boundary adapters only delegate
  (`SessionCpuControlBackend` → `ProcessService`, `AppSessionOptimizationProvider` → coordinator,
  `AppLibraryProvider`/`AppBackgroundAppProvider`, shared `LibraryLaunchReservationService`).
- Persistence: `AtomicFileService` (general), `DeviceRecordStore` (deliberately separate writer,
  publish-after-persist — P3, do not touch).
- Companion: 7 controllers (`api/v1/*`), `PairingEngine`, `CompanionAuthMiddleware`,
  `CompanionScopes` (16 constants), `CompanionServer`, `TelemetryWebSocketHandler`
  (ticket + generation guards, 3 s / 30 s retry), vanilla-JS frontend (7 modules, `textContent` only).
- Developer-only: `FrameHub.BenchmarkHarness` (not referenced by App or installer — VERIFIED).

## 3. Experimental / research (not product commitments)

- Benchmark comparison methodology + advanced PresentMon / frame-generation research
  (ROADMAP Experimental, unchanged this NOC).
- Additional Windows utilities subject to safety/reversibility review (unchanged).
- PresentMon v2.6.0 pilot (upstream 2026-09-21): evaluated as variant B, **deferred** —
  service/DLL absent on this machine, pin change without ABI validation would be speculation (NOT RUN).
- Native ETW/embedded backend (variant C): **rejected** (correctness risk, elevation per capture,
  months of multi-vendor telemetry work, anticheat exposure). Static-link PresentMon core (variant D):
  research-only. Full comparison: researcher report `FRAMEHUB-PRESENTMON-ABCD.md`.

## 4. Dormant (1)

- `FrameHub.GameData/` (JSON data + manifest, no `.csproj`, absent from `FrameHub.slnx`)
  and `FrameHub.Core/Services/GameData/GameDataService.cs` + `GameDataModels.cs`:
  **zero production consumers** (confirmed by Researcher, Coder and doc-audit greps across
  `.cs`/`.xaml`/`.csproj`/`.slnx`/DI/reflection). Banner added to its README this NOC.
  Decision adopt-vs-remove is P3 and needs a separate reviewed change — nothing deleted.

## 5. Dead (0 confirmed)

- No confirmed dead production code. `BENCHMARKING.md:94` "retired backend values" is a
  future-guard CLI sentinel (`BenchmarkHarnessOptions.cs:65`); benchmarks were born as
  Service/API (`f477ba3`, 2026-08-09) and `PresentMonApiCaptureBackend` is the only
  `IBenchmarkCaptureBackend` implementation. Nothing to remove.

## 6. What changed this NOC (code, REPORTED from QA review)

- `e70e967` + `2bd22cb` — `BenchmarkViewModel.OnCoordinatorStateChanged`: stale-snapshot guards
  (`BenchmarkViewModel.cs:275-276`) close the late-Capturing-overwrites-terminal-state race.
- `d9bfc39` — `DashboardViewModel : IDisposable` (`DashboardViewModel.cs:439-446`),
  first-disposed from `ShellViewModel.Dispose` (`ShellViewModel.cs:314-322`),
  production path `MainWindow.OnClosed` → `DataContext as IDisposable` (`MainWindow.xaml.cs:548-550`).
- 4 new tests (651 → 655): `LateCapturingSnapshot_…`, `CaptureFailure_RemainsTerminalUnderRepeatedCapture`,
  `DashboardViewModel_Constructor_Subscribes…`, `DashboardViewModel_Dispose_Unsubscribes…`.
- No new services, owners, timers, or polling loops (REUSE-FIRST clean).
