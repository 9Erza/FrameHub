# FrameHub agent instructions

## Start here

- Read `docs/ARCHITECTURE.md` and these canonical documents before planning or editing:
- `docs/architecture/OVERVIEW.md`
- `docs/architecture/SERVICE-CATALOG.md`
- `docs/architecture/FEATURE-MAP.md`
- `docs/architecture/BACKGROUND-WORK.md`
- `docs/architecture/INVARIANTS.md`
- `docs/architecture/REFACTOR-CANDIDATES.md`
- Read `docs/BENCHMARKING.md` before benchmark or PresentMon changes.
- Read `docs/ROADMAP.md` before changing product scope.
- Inspect the working tree first and preserve unrelated user changes.
- Verify a responsibility has no owner before adding a service.
- Prefer the smallest change that preserves product behavior.

## Layer boundaries

- `FrameHub.Core` owns domain models, Windows/native services, persistence primitives, and benchmark mechanics.
- `FrameHub.App` owns WPF presentation, runtime composition, and Companion adapters.
- `FrameHub.Companion` owns HTTP/WebSocket transport, authentication, scopes, DTOs, and static presentation.
- Companion is transport/presentation; backend business rules stay in Core/App authorities.
- `FrameHub.GameData` is dormant research, not the production CS2 authority.
- Production CS2 behavior belongs to `Cs2OptimizationService` and Library presentation.

## Process observation and mutation

- Check `ProcessScannerService` and `ProcessObservationSnapshotProvider` before process-related work.
- `ProcessObservationSnapshotProvider` is the shared on-demand full-enumeration primitive.
- It has a short TTL, single-flight refresh, and no timer.
- `ProcessScannerService` owns domain projections and the sole per-process CPU sampler.
- Do not add a persistent process scanner without explicit documented justification.
- Do not add `Process.GetProcesses` or `Process.GetProcessesByName` casually; first reuse existing batch/targeted paths.
- FrameHub's own observation and polling overhead matters in gaming workloads.
- Prefer one batch observation for many Library items or candidates.
- Cached observation is only for display, discovery, and non-destructive decisions.
- Cached observation must never authorize Kill, CloseMainWindow, suspend, resume, priority, affinity, or CPU Set changes.
- Before destructive process mutation, freshly reacquire the targeted process.
- Revalidate PID, start time, process name, and executable path where required.
- Preserve PID-reuse protection and fail closed when identity is unavailable.
- `ProcessService` owns priority, affinity, and CPU Set native work.
- `ProcessSuspendService` owns suspend/resume native calls and live identity revalidation.
- Background App Stop uses fresh targeted identity acquisition plus terminator revalidation.
- Never replace safety revalidation with cached observation.

## Benchmark and PresentMon authority

- `BenchmarkCaptureCoordinator` is the sole benchmark lifecycle/reservation authority.
- Use its external-mutation arbitration for mutations that alter the benchmark environment.
- Automatic profile mutations skip while a benchmark is accepted, reserved, or running.
- Manual profile mutations reject before OS mutation while arbitration is unavailable.
- Do not create another benchmark state check or state machine.
- `LivePerformanceTelemetryService` owns live PresentMon.
- `ActiveGameMonitor` owns live/Companion active-game state.
- Benchmark capture uses the existing `PresentMonApiCaptureBackend` source/protocol.
- Do not add a PresentMon owner outside the existing preemption protocol.
- Do not change benchmark mathematics without an explicit requirement and dedicated tests.
- Preserve exact target identity validation before capture.

## Session Optimization authority

- `SessionOptimizationCoordinator` is the sole Session lifecycle authority.
- Keep its WAL, recovery, mutation gate, taskbar recovery, and shutdown behavior cohesive.
- Its query path owns settings, Library games, rule/candidate projection, and group filtering.
- View models own UI properties, commands, localization, and scheduling only.
- `ProcessSuspendService` stays focused on native mutation and snapshot-to-candidate primitives.
- Do not add a second Session lifecycle, recovery journal, or generic scanner.

## Library and Companion control

- `AppLibraryProvider` owns regular remote Library listing and launch.
- `AppBackgroundAppProvider` owns Background App listing and Start/Stop.
- `LibraryLaunchReservationService` is their shared per-item cooldown authority.
- Keep API routes and opaque server-side Library IDs stable.
- Never expose paths, PIDs, command lines, credentials, or environment data in remote DTOs.
- Remote mutations require explicit write scopes; read does not imply write.
- New scopes are never automatically granted to paired devices.
- Future remote profile mutations must use benchmark mutation arbitration.
- Never accept arbitrary paths, PIDs, process names, priority, affinity, or CPU Sets from Companion.
- Long-lived connections must respect credential revocation and revalidation.

## Persistence and trust

- Reuse each domain's persistence owner.
- `SettingsService` owns settings; `ProfileService` owns profiles; `LibraryService` owns Library persistence.
- `SessionStateService` owns Session recovery state; `DeviceRecordStore` owns Companion devices/scopes.
- Use `AtomicFileService` when its backup/replacement semantics fit.
- Keep one logical writer per persistent domain.
- `DeviceRecordStore` is a deliberate persist-before-publish/faulted-state exception.
- Do not weaken paired-device durability or persist plaintext credentials.
- Keep Session recovery writes durable before native mutation.
- Preserve Library sanitization and server-side trusted-item reloads.

## Hardware and background work

- Reuse `HardwareMonitorService`; do not create another sensor backend.
- Hardware access remains lease-controlled and opt-in.
- Document every timer, recurring task, retry loop, interval, start, and stop condition.
- Update `docs/architecture/BACKGROUND-WORK.md` whenever recurring work changes.
- Do not add Companion polling without explicit justification.
- Preserve frontend status 1 s, Session 4 s, telemetry fallback 1 s, and WebSocket reconnect behavior unless explicitly changed.
- Keep the CS2 process timer active only while CS2 presentation is selected.

## Frontend rules

- Keep Companion frontend code vanilla JavaScript.
- `app.js` is bootstrap/shared composition; domains live in existing module files.
- Preserve `sessionStorage` credentials and `localStorage` language behavior.
- Preserve pairing fragment cleanup, 401/403 distinctions, scopes, and WebSocket generation guards.
- Use `textContent` and other safe DOM APIs for dynamic backend text.
- Do not introduce `innerHTML` for dynamic content.
- Backend coordinators stay authoritative; frontend modules render DTOs and call routes.
- Keep EN/PL localization keys in parity.

## Change discipline

- Do not create interfaces merely because a class exists.
- Create a service only when it owns a real responsibility, lifecycle, resource, persistence boundary, or policy.
- Do not split files only because they are large.
- Keep benchmark coordinator, live telemetry, and localization cohesive absent concrete need.
- Avoid abstractions that obscure Windows identity and safety rules.
- Remove dead code only after whole-solution search and compile/test confirmation.
- Update architecture docs when ownership, trust, persistence, or background work changes.
- Keep product roadmap statements separate from refactor candidates.

## Validation

- Baseline: `git status -sb` and `git status --short --untracked-files=all`.
- Build: `dotnet build --configuration Release`.
- Desktop: `dotnet test FrameHub.Tests/FrameHub.Tests.csproj --configuration Release --no-build`.
- Companion: `dotnet test FrameHub.Companion.Tests/FrameHub.Companion.Tests.csproj --configuration Release --no-build`.
- JavaScript: run `node --check` for every file in `FrameHub.Companion/wwwroot/js`.
- Diff hygiene: `git diff --check`.
- Final scope: `git status --short --untracked-files=all` and `git diff --stat`.
- Report exact warnings/errors, test counts, changed files, and deliberate non-changes.
- Never reset, restore, stash, clean, rebase, amend, commit, push, tag, or publish unless explicitly requested.
