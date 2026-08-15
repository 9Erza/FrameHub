# Architecture overview

## Projects and layers

- `FrameHub.Core`: domain models, Windows/native services, process observation/mutation, Library persistence/scanning, Session primitives, hardware monitoring, and benchmark capture/analysis/storage.
- `FrameHub.App`: WPF presentation, localization, application composition, authoritative Session coordination, and adapters exposing existing backend behavior to Companion.
- `FrameHub.Companion`: ASP.NET Core HTTP/WebSocket host, authentication/scopes/rate limits, DTO contracts, and static vanilla-JavaScript UI.
- `FrameHub.GameData`: dormant declarative integration research. Production does not reference it; CS2 uses `Cs2OptimizationService` directly.
- `FrameHub.BenchmarkHarness`: developer-only capture harness outside the installed WPF runtime.
- `FrameHub.Tests` and `FrameHub.Companion.Tests`: Core/Desktop and Companion regression suites.

## Runtime composition and authorities

`ShellViewModel` creates one `AppRuntimeService`. Runtime loads settings, profiles, and topology; composes process observation, benchmark, Active Game, live telemetry, Session, Library, Background App, and Companion adapters; then starts only configured or required work. Hardware sensors remain lease-controlled, and a 200 ms timestamped snapshot prevents simultaneous consumers from repeating a backend refresh.

- `BenchmarkCaptureCoordinator`: benchmark acceptance, reservation, lifecycle, cancellation, terminal state, PresentMon preemption, and external-mutation arbitration.
- `SessionOptimizationCoordinator`: Session start/restore, WAL/recovery, taskbar state, concurrency, and shared query/policy projection.
- `ProcessScannerService`: domain process projections and the one CPU sampler.
- `ProcessObservationSnapshotProvider`: on-demand short-TTL, single-flight, non-destructive full-process metadata.
- `ProcessService`: priority, affinity, and CPU Sets.
- `ProcessSuspendService`: suspend/resume native calls and fresh identity validation.
- `LivePerformanceTelemetryService`: live PresentMon ownership.

## Process observation

Non-destructive consumers request `ProcessObservationSnapshotProvider.GetSnapshotAsync`. A fresh snapshot is reused for 250 ms; an expired snapshot starts one enumeration task; concurrent callers await it. The provider owns no timer. Library running state, Background App list state, benchmark game discovery, Active Game, and Session snapshot capture use this metadata.

The UI process list remains the sole CPU sampler and performs its own full enumeration because CPU deltas and working set need live handles. Profile watching uses targeted name queries. Mutations reacquire targeted processes and validate identity; cached observation never authorizes mutation.

## Companion

`CompanionServer` hosts controllers and static files. Authentication middleware verifies paired-device credentials and explicit scopes. Controllers call provider contracts; App adapters invoke existing authorities. Regular Library and Background App capabilities use separate providers, with one shared `LibraryLaunchReservationService`. Remote DTOs expose opaque IDs and presentation data, not OS identities.

Frontend files are bootstrap (`app.js`), authentication/WebSocket transport, telemetry, benchmarks, Library/Background Apps, Session Optimization, and localization. They contain transport and presentation behavior only.

## Benchmark and Session flows

Benchmark discovery resolves a Library game from non-destructive observation. `BenchmarkCaptureCoordinator` owns the reservation before acceptance, preempts live PresentMon, revalidates PID/name/start/path, runs `PresentMonApiCaptureBackend`, analyzes frames, and stores sessions. Environment mutations cannot acquire a lease while capture is reserved or active.

Session UI and Companion call `SessionOptimizationCoordinator`. Its query path loads settings/Library games, projects rules, filters groups, and creates previews. Start obtains benchmark arbitration, writes recovery intent before native mutation, and delegates suspend/resume to `ProcessSuspendService`. Restore stays conservative when identity or durable state is ambiguous.

## Persistence

Settings, profiles, Library items, Session recovery, and benchmark sessions retain domain owners. Most JSON stores use `AtomicFileService`. `DeviceRecordStore` deliberately retains private temp-plus-overwrite I/O because its locked persist-before-publish and faulted-state behavior is security-sensitive and differs from general backup semantics. Pairing credentials are protected; plaintext credentials are not persisted.
