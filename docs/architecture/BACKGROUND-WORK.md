# Background work inventory

This file is canonical. Update it whenever a timer, loop, interval, start condition, or stop condition changes.

The Work/I/O and Cost/overlap columns identify process enumeration, native/hardware, network, and disk activity when present; unmentioned categories are none. `ProcessObservationSnapshotProvider` and the 200 ms hardware snapshot are on-demand caches, not background work and not timers.

| Component | File | Interval | Start / stop | Work and I/O | Cost / overlap |
|---|---|---:|---|---|---|
| Profile watcher | `AppRuntimeService.cs` | configured 1–30 s, default 2 s | App runtime start / runtime dispose or manual stop | Targeted `GetProcessesByName`; profile identity checks; priority/affinity/CPU Set mutation if benchmark lease succeeds | Medium when profiles enabled; no full enumeration; skips during benchmark without queueing |
| Process page refresh | `ProcessesViewModel.cs` | configured 1–10 s | Processes page `Start` / `Stop` | Full enumeration, user-process filtering, working set and CPU delta sampling | Highest process-observation cost; sole CPU sampler |
| Session auto detection | `SessionOptimizationViewModel.cs` | 3 s | Auto mode enabled / disabled or dispose | Batch running-game lookup through shared observation; may ask coordinator to start/restore | Full-snapshot request; can share short-TTL observation with nearby consumers |
| Active Game monitor | `ActiveGameMonitor.cs` | 2 s | Companion server active / server stop or runtime dispose | Library load and game detection through shared observation | Full-snapshot request; overlaps Session/benchmark discovery but can share snapshot |
| Benchmark target detector | `BenchmarkViewModel.cs` | 5 s | Benchmark presentation detection enabled / view-model stop | Detects running Library games | Full-snapshot request in its detector; no CPU sampling or mutation |
| Benchmark progress | `BenchmarkViewModel.cs` | 200 ms | Capture UI active / terminal state | Presentation countdown/progress only | Low; no process enumeration or I/O |
| CS2 process check | `LibraryViewModel.cs` | 2 s | A CS2 Library item is selected / selection leaves CS2 or view model disposes | Targeted `GetProcessesByName` to gate config editing | Low targeted query; handler attaches once and is detached on disposal |
| Hardware page refresh | `HardwareViewModel.cs` | configured 1–10 s | Hardware page active AND persisted `HardwareMonitorEnabled` true / leaving page, setting disabled, or dispose | Lease-backed metrics; LibreHardwareMonitor `Update()` and RAM fallback on cache miss | Native/hardware; opt-in; shares one backend and 200 ms runtime snapshot |
| Companion snapshot publisher | `AppTelemetrySnapshotProvider.cs` | 500 ms | Companion server starts / stops | Reads hardware only when `HardwareMonitorEnabled` is true and a consumer exists; combines Active Game and live performance | Medium with connected hardware consumer; no network itself |
| Telemetry WebSocket sender | `TelemetryWebSocketHandler.cs` | 500 ms | Authenticated WS connected / disconnect/cancel | Sends current snapshot; registers one hardware consumer while connected (sensors open only while `HardwareMonitorEnabled` is true) | Network; shares published snapshot/backend |
| Live PresentMon | `LivePerformanceTelemetryService.cs` | 250 ms normal; 5 s retry | Companion runtime starts / stops; preempted by benchmark | PresentMon Service/API queries for current Active Game | Native, performance-sensitive; sole live PresentMon owner |
| Benchmark capture read loop | `PresentMonApiFrameSource.cs` | 20 ms during capture | Coordinator starts capture / duration, cancel, failure | PresentMon frame consumption for exact PID | Native; capture-only; live owner is preempted first |
| Companion benchmark status poll | `benchmarks.js` | 1 s | Frontend init / page unload | `GET /api/v1/benchmarks/status`; deduplicated result/history fetches | Network; unchanged by modularization |
| Companion Session poll | `session-optimization.js` | 4 s | Frontend init / page unload | `GET /api/v1/session-optimization` and `GET /api/v1/session-optimization/cpu` with in-flight guards | Network; unchanged 4 s interval |
| Companion telemetry fallback | `auth-transport.js` | 1 s | WebSocket unavailable / WS open or unload | `GET /api/v1/telemetry`, request/generation ownership checks | Network fallback only; stopped on successful WS |
| Companion WS reconnect | `auth-transport.js` | timeout-driven, including 30 s scope retry | Connection/ticket failure / credential change, connect, unload | Ticket request and WebSocket reconnect | Network; one cancellable timeout and generation guards |
| Telemetry stale marker | `telemetry.js` | 3 s one-shot after snapshot | Each telemetry render / next snapshot or teardown | Clears stale live values | Presentation only |

## Full-process recurring observation count

Four feature-activated recurring loops may request a full process view: Processes page refresh, Session auto detection, Active Game monitor, and Benchmark target detection. The consolidation pass added zero timers. Three discovery consumers can reuse short-lived observation where they share the runtime provider; the Processes page keeps the one CPU-sampling enumeration. Background App and Library refreshes are on-demand batch requests, not loops. Dashboard Gaming Mode (M10.2) performs on-demand batch observation only when the Dashboard is activated or a game is selected; it owns no timer and added no recurring work.

## Runtime scenarios

- Idle, no feature pages/Companion: profile watcher performs targeted enabled-profile queries; shared full observation is dormant; hardware and PresentMon are off.
- Game running with Desktop pages: active page timers run; Session auto may request batch observation; CS2 targeted check runs only on selected CS2 presentation.
- Companion connected: Active Game (2 s), published snapshot (500 ms), live PresentMon (250 ms), and WS send (500 ms) run; hardware opens only through the connection consumer while the persisted `HardwareMonitorEnabled` setting is true. Frontend REST telemetry polling stops when WS opens.
- Benchmark active: live PresentMon is preempted; capture uses the existing backend; profile watcher skips mutations; manual profile, Session, and Background App mutations cannot obtain arbitration. UI progress/status loops continue.
