# FrameHub NOC technical audit (point-in-time, 2026-10-24)

Status: point-in-time. Canonical follow-ups belong in
`docs/architecture/REFACTOR-CANDIDATES.md`, `docs/architecture/SERVICE-CATALOG.md`
and `docs/ROADMAP.md` — this file records the NOC state with evidence links.

- Baseline: `main` @ `0c6efc24` (= BASE), v0.7.1. Head: `noc/t_2e7bd27e-0c6efc2` —
  HEAD kodu `f53e91e` (5 local code commits, no push); HEAD gałęzi = niniejszy commit
  dokumentacyjny (treść kodu bez zmian względem `f53e91e`).
- Severity rubric: `docs/agents/REVIEW.md` (P0–P3). QA verdict: **PASS WITH WARNINGS** (REPORTED,
  `QA_REVIEW.md`); acceptance is administrative (independent QA run outside crashed Kanban worker).
- VERIFIED = inspected in the worktree diff/code during this task; REPORTED = taken from
  Researcher/Coder/QA reports without re-running their procedures.

## 1. Fixed and independently verified this NOC (VERIFIED in diff, execution REPORTED)

| ID | Sev (was) | Location | Finding → fix |
|---|---|---|---|
| NOC-1 | P3 flake | `FrameHub.App/ViewModels/BenchmarkViewModel.cs:275-276`, root cause `FrameHub.Core/.../BenchmarkCaptureCoordinator.cs:515-534,558-590` + unconditional `Capturing` branch (was `:270-275`) | Async state-notification drain let a late `Capturing` snapshot overwrite a locally set terminal state (`Failed`/`Completed`/`Cancelled`). Fix: two in-`Action` stale-snapshot gates (`_localTerminalApplied`; `snapshot.IsActive && !_coordinator.IsActive`). Negative controls REPORTED: deterministic test fails on `e70e967` and on BASE (3/3) with the baseline signature `Expected <Failed>, Actual <Capturing>`; HEAD green incl. 10/10 filtered and 18/18 under 6× load |
| NOC-2 | P2 | `FrameHub.App/ViewModels/DashboardViewModel.cs:146-149,439-446`, `FrameHub.App/ViewModels/ShellViewModel.cs:107,314-322`, `FrameHub.App/MainWindow.xaml.cs:541-550` | 4 runtime/coordinator subscriptions had no unsubscribe (Researcher C2). Fix: named handlers + idempotent `IDisposable.Dispose`, disposed first from `ShellViewModel.Dispose`, production path `MainWindow.OnClosed` confirmed. No re-creation site (single construction at `ShellViewModel.cs:107`) |

Reuse/single-owner (VERIFIED): no new services, managers, timers, samplers, PresentMon owners
or hardware backends; Companion adapters still delegate only.

## 2. Open findings (do NOT treat as fixed)

| ID | Sev | Location | Statement + required action |
|---|---|---|---|
| OPEN-P2-1 | P2 → FIXED (etap-0, `f53e91e`) | `FrameHub.Tests/BenchmarkUiCoreTests.cs:351`, `FrameHub.App/ViewModels/BenchmarkViewModel.cs:382-402` (`DispatchIfRequired`) | Deterministic `LateCapturingSnapshot_…` test **could** become a **vacuous PASS** depending on in-process test order (REPORTED probe). Fixed two-sided: (1) test-side **positive injection control** (fresh VM in `Idle`, inject `Capturing`, assert `State == Capturing`) fails loudly instead of passing vacuously; (2) production-side `action()` fallback in `DispatchIfRequired` when the dispatcher is dead/shutting down (same graceful degradation as no-`Application`; handler guards disposed VMs), making delivery order-independent. Negative controls on `e70e967` code: LateCapturing fails with the baseline signature `Expected <Failed>, Actual <Capturing>`; countdown test fails `Expected <Waiting>, Actual <Idle>`. Full evidence in etap-0 report. |
| OPEN-P3-1 | P3 | `GamingQuickActionServiceTests.cs:266` | Constructor-subscription test never calls `Dispose` — leaves subscriptions to end of test process (test hygiene) |
| OPEN-P3-2 | P3 | `BenchmarkUiCoreTests.cs:333` | 25× `CaptureFailure_RemainsTerminalUnderRepeatedCapture` tripwire is **not decisive** (passes 3/3 on BASE — narrow race window); the deterministic test is the real guard; note it in a comment so the team does not rely on the tripwire |
| OPEN-P3-3 | P3 → FIXED (etap-0, `f53e91e`) | `BenchmarkViewModel.cs:474-515` | No test for countdown/capture-progress visibility during local await (fix `2bd22cb` restored it; QA verified only with an out-of-repo probe) — added `InOrderWaitingSnapshot_DuringLocalAwait_KeepsCountdownVisible` (real coordinator + controllable backend, countdown 6 s): in-order `Waiting` applied during local await with `RemainingCountdown > 0`. Fails on `e70e967` blind-guard code (`Expected <Waiting>, Actual <Idle>`). |
| OPEN-P3-4 | P3 | `BenchmarkViewModel.cs:257-258` (flags) | `_isLocallyAwaitingCapture` / `_localTerminalApplied` lack `volatile`; safe in production (UI-thread gates) but formally unsynchronised on the no-`Application` test path |
| F-2 | P3 | `FrameHub.Core/Services/AppPaths.cs:10-13` | No data-directory override: `SpecialFolder.ApplicationData` ignores `APPDATA` redirection, so tests/dev builds write to the shared `%APPDATA%\FrameHub\FrameHub.log` (REPORTED: +34 661 B of concurrent-test noise during the 240 s idle soak). Contaminates before/after measurements; fix needs a reviewed change — **not done this NOC** |
| F-3 | P3 | `FrameHub.Companion/wwwroot/js/*.js` (3003 LOC) | Zero JS logic tests (no `package.json`, no `*.test.js`); frontend tests assert static content only. JS untouched this NOC (change without a harness would be unprotected) |
| GAME-DATA | P3 | `FrameHub.GameData/`, `GameDataService.cs`, `GameDataModels.cs` | Dormant, zero consumers (see inventory). Adopt-or-remove needs a separate reviewed decision — nothing deleted this NOC |
| PM-2.6.0 | P1 pilot (deferred) | `installer/FrameHub.iss:8`, `Prepare-PresentMonPrerequisite.ps1:9`, `PresentMonApiInterop.cs:6` (ABI `PresentMonAPI2 v3.3`) | Upgrade pilot deferred: service/DLL absent on this machine, pin change without ABI validation = speculation (NOT RUN). Gate: `PresentMonIntrospectionTests` + `BenchmarkHarness --presentmon-api-dll <2.6.0>` → pin + SHA-256 → QA regression; rollback = revert pin |
| UPDATE-SIG | P2 note | `UpdateService.cs` | No update-signature verification (threat-model note from Researcher, not a blocker) — future work, not this NOC |
| LEAKS-REST | info | `HardwareViewModel` (2×), `ProfilesViewModel` (1×), `ShellViewModel` (3×), `MainWindow` (1×) | Same lambda-subscription pattern as NOC-2, pre-existing, out of this diff — candidate for a separate card |

P0: **none**. P1: **none open** (PM-2.6.0 is a gated pilot, not an accepted defect).

## 3. Architecture decisions recorded (no code change, REPORTED from Researcher + Coder + QA)

- **PresentMon A+B**: keep proven Service/API v2.5.1; v2.6.0 only as validated pilot. Variant C (own ETW backend) rejected; D research-only. Rationale: correctness risk, per-capture elevation, multi-vendor telemetry cost, anticheat exposure.
- **GameData**: dormant confirmed; no deletion without adopt-vs-remove review.
- **"Retired backends"**: negative — CLI sentinel only, nothing to remove.
- **Timers**: `BACKGROUND-WORK.md` (17 rows) is the canonical inventory; `RUNTIME-OVERHEAD.md` §5 is a subset. Pointer added this NOC; no third inventory created.
- **Target web architecture** (plan only, not implemented): LAN-only + pairing/scopes unchanged; PWA = manifest + SW cache of UI assets only (never `/api/*`); remote access only via user tunnel, never direct Kestrel exposure; future `v2` = versioned DTOs + contract tests.

## 4. Scope not investigated (NOT RUN, with cause)

Benchmark capture e2e / live PresentMon / preemption (service not installed, no game);
live session-optimization mutation and live Riot protection (prohibited);
Companion HTTP/WS runtime (`CompanionEnabled=false`, no data-dir isolation);
hardware sensors; UI cycles / DPI / themes / tray-Exit shutdown (no UIA harness);
soak > 240 s and interactive soak; on-device mobile / LAN; installer execution;
GitHub CI (no push — local parity of CI commands only); any REA materials (forbidden,
separate private card `t_7cb9e99f`, no dependency, no data in repo).
