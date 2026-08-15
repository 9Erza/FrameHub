# Enforced architecture invariants

## Authority

- `BenchmarkCaptureCoordinator` is the only benchmark lifecycle/reservation authority.
- `SessionOptimizationCoordinator` is the only Session lifecycle/recovery authority.
- `ProcessService` performs priority, affinity, and CPU Set native operations.
- `ProcessSuspendService` performs suspend/resume and revalidates identities.
- `LivePerformanceTelemetryService` owns live PresentMon; benchmark capture uses the configured preemption contract.
- `HardwareMonitorService` is the single sensor backend and is controlled by App runtime leases; the runtime reuses metrics for 200 ms across consumers.

## Process observation and mutation

- Shared all-process observation is on demand, short-lived, timestamped/generation-based, and single-flight.
- The shared provider has no timer and does no work without a consumer.
- Library and Background App list projections evaluate all items from one snapshot.
- The process UI CPU sampler remains separate and singular because it needs CPU-time deltas.
- Cached observation is never passed as authority for Kill, close, suspend, resume, priority, affinity, or CPU Set mutation.
- Background App Stop freshly targets by process name, captures PID/start/name/path, then the terminator reacquires by PID and checks all identity fields before close/kill.
- `OptimizationService` reacquires each PID and matches PID/start/name/path before scheduling mutation.
- `ProcessSuspendService` reads live identity before suspend/resume and fails closed on ambiguity.

## Benchmark environment

- Benchmark acceptance creates an authoritative reservation before returning success.
- External mutation leases and benchmark reservations exclude one another under one coordinator lock.
- Profile-watcher mutations skip without queueing when a benchmark reservation is unavailable.
- Manual profile mutation returns `SKIPPED_BENCHMARK_ACTIVE` before OS mutation when arbitration is unavailable.
- Session and Background App mutations use the same arbitration authority.

## Companion trust

- Companion controllers use provider contracts; they do not implement benchmark, process, or Session business rules.
- Remote objects are selected by opaque server-side Library IDs and reloaded from trusted persistence.
- Background App control requires explicit opt-in, eligibility checks, and dedicated read/write scopes.
- A write scope depends on its read scope; new scopes are not auto-granted.
- Remote DTOs do not expose executable paths, PIDs, command lines, environment data, or credentials.
- Dynamic frontend content is assigned through safe DOM APIs, not dynamic `innerHTML`.

## Persistence

- Session intent is persisted before suspend/taskbar mutation; recovery remains conservative after ambiguous failure.
- General domain stores use their existing owners and backup-aware atomic writes where configured.
- `DeviceRecordStore` publishes in-memory device changes only after its write succeeds and enters a faulted state after persistence failure.
- Paired-device credentials are protected at rest; plaintext credentials are not persisted.

## Layering and background work

- Core/App own backend behavior; Companion and its JavaScript modules own transport/presentation.
- No frontend module reconstructs benchmark math or Session candidate policy.
- No new process, PresentMon, hardware, or frontend polling loop was introduced by the consolidation pass.
- Every recurring loop is listed in `BACKGROUND-WORK.md` and must be updated with code changes.
