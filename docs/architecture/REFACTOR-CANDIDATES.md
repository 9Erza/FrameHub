# Remaining refactor candidates

Only evidence-backed candidates remaining after the consolidation pass are listed here.

## P1 — Library/CS2 presentation extraction

- Problem: `LibraryViewModel` still combines Library scanning/editing/profiles/launch with the large CS2 analysis/config/backup/autoexec workflow.
- Evidence: one view model remains roughly 1,700 lines with many CS2-bound properties and commands.
- Direction: extract a binding-compatible `Cs2OptimizationViewModel` or cohesive presentation object when the XAML can be migrated deliberately.
- Risk: high binding and lifecycle blast radius; config mutation/backup behavior is safety-sensitive.
- Blast radius: Library XAML, Shell navigation, localization, CS2 tests, command enablement.
- Revisit when: the next CS2 presentation feature is scheduled or dedicated UI integration tests exist.

## P2 — Session query service extraction

- Problem: shared query/policy ownership is now reached through coordinator methods, which keeps the ViewModel clean but adds read-only responsibilities to the coordinator surface.
- Evidence: coordinator exposes Library loading, rules, groups, candidates, and snapshot capture in addition to lifecycle.
- Direction: introduce one `SessionOptimizationPreviewService` shared by coordinator and UI only if query growth becomes material.
- Risk: medium; constructor/test seams and safety-critical candidate parity could drift.
- Blast radius: coordinator, Session ViewModel, App provider, coordinator tests.
- Revisit when: another consumer needs preview semantics or rule policy expands.

## P2 — Process observation projection efficiency

- Problem: Library matching compares every observed process with every target.
- Evidence: batching removes N system enumerations, but the in-memory projection is O(processes × targets).
- Direction: index targets by normalized name/path inside `ProcessScannerService` if large libraries demonstrate cost.
- Risk: medium because path-first/name-fallback semantics must remain exact.
- Blast radius: Library/Background running-state tests.
- Revisit when: measured projection time, not OS enumeration, becomes visible.

## P3 — Benchmark test temp-scope helper

- Problem: a transient `raw-frames.json` cleanup lock can still fail teardown on Windows.
- Evidence: baseline first run observed 327 pass + 1 cleanup failure; immediate rerun passed 328.
- Direction: shared awaited temp scope with bounded cleanup retry that still reports leaked resources.
- Risk: low-to-medium; excessive retries could hide ownership bugs.
- Blast radius: benchmark UI/storage test fixtures only.
- Revisit when: the transient repeats or more fixtures duplicate cleanup code.

## P3 — Declarative GameData decision

- Problem: `FrameHub.GameData` exists but production CS2 bypasses it.
- Evidence: production references `Cs2OptimizationService`; `GameDataService` has no production consumers.
- Direction: either adopt it explicitly for a future multi-game contract or remove it in a separately reviewed change.
- Risk: unclear future packaging/data contract.
- Blast radius: project structure and future integration roadmap.
- Revisit when: a second game integration is planned.

## P3 — DeviceRecordStore atomic-writer convergence

- Problem: paired-device persistence intentionally uses a private temp-plus-overwrite writer instead of `AtomicFileService`.
- Evidence: `DeviceRecordStore` couples persist-before-publish, locking, protected credentials, and a permanent fault state; general atomic writes also create backup semantics.
- Direction: converge only if the file-I/O primitive can be substituted without changing any device-store state or failure behavior.
- Risk: high for pairing durability and credential security relative to the small deduplication benefit.
- Blast radius: Companion device persistence, pairing, revocation, and failure tests.
- Revisit when: the general atomic writer supports the exact device-store contract or device persistence otherwise changes.
