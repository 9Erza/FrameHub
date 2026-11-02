# FrameHub audit 2026-11 — architecture, RAM, fixes, validation

Branch: `fix/audit-ram-lifecycle` → `main`. Baseline `main` = `a7d16cc` (PR #23).
Date: 2026-11-02. Scope: full repo (WPF/MVVM, services, Companion, JS), RAM focus.

## 1. Architecture / code quality assessment

Layering and single-owner authorities hold: `ProcessService` (native scheduling),
`SessionOptimizationCoordinator` (session lifecycle), `BenchmarkCaptureCoordinator`
(benchmark lifecycle/arbitration), `LivePerformanceTelemetryService` (live PresentMon),
`ProcessSuspendService` (suspend/resume), `ProcessObservationSnapshotProvider`
(TTL 250 ms, no timer), `ProcessScannerService` (sole CPU sampler), `AppRuntimeService`
(composition/profile watcher), `HardwareMonitorService` (lease-gated). No parallel
owners found; no new service/manager/timer/backend was introduced by this audit.
`BACKGROUND-WORK.md` matches the code (one P1 note below on two sub-second
one-shot delays that are correctly not listed as recurring loops).

Overall: healthy boundaries, strongest areas are shutdown budgeting (8 s envelope,
fail-safe close), WAL persist-before-mutation, Riot hard blocks on all mutation
paths, Companion scope enforcement incl. loopback write protection, and
`ConfigureAwait(false)` discipline in library/server code.

## 2. Confirmed issues (all fixed unless noted)

### P0 — retention / unbounded growth (fixed)

- **P0-1** All 9 VMs composed eagerly and retained by `ShellViewModel`; `Dispose`
  covered only 6/9; `Logs/Profiles/Settings` had no `Dispose` at all.
  (`ShellViewModel.cs:168-181,468-484`)
- **P0-2** 10 event subscriptions converted from lambdas to named handlers with
  `-=` on Dispose: shell×6 (`ShellViewModel.cs`), `ProcessesViewModel`
  coordinator pin, `HardwareViewModel`×2, `SessionOptimizationViewModel`
  `Games.CollectionChanged`, new `Profiles/Settings/Logs/Dns` disposals.
- **P0-3** `LogsViewModel` additionally pinned by the process-wide cached default
  `ICollectionView.Filter` delegate over the runtime-owned `Activity` collection;
  proven by failing-then-passing GC test. Dispose now clears `Filter`.
  (`LogsViewModel.cs`)
- **P0-4** `IconSource` getters in Library/Session/Benchmark VMs ran
  `File.Exists + ExtractAssociatedIcon + CreateBitmapSourceFromHIcon` per binding
  evaluation with no cache and no `Freeze()`. Replaced by one bounded (96, LRU)
  frozen-image cache: `FrameHub.App/Helpers/GameIconCache.cs` (pure presentation
  helper, no domain logic — passes the New Abstraction Gate).
- **P0-5** `AppRuntimeService._failureLogThrottle` keyed per process|message with
  no bound. Now prunes expired entries at 500, full clear as backstop.
- **P0-6** Companion `library.js` `iconUrlCache` Map of un-revoked blob URLs.
  Now capped at 200 FIFO with `URL.revokeObjectURL` on eviction.

### P1 — hot-path / lifecycle hygiene (fixed where safe)

- **P1-1** `DnsDiagnosticsPanelViewModel.CompareAsync` disposed the CTS without
  `Cancel()` first and leaked it on validation early-returns. Now cancel-first
  rotation + dispose on early return + `IDisposable` (owned by Settings VM).
- **P1-2** Benchmark history `ListBox` was the only unvirtualized list; now
  `IsVirtualizing + Recycling + CanContentScroll` like all other lists.
  (`BenchmarkView.xaml:278`)
- Session `Dispatcher.CurrentDispatcher` usage (flagged P1 by audit) verified as
  false positive: called only from UI-thread event paths; sibling VMs use the
  captured-dispatcher pattern where cross-thread callbacks exist. No change.

### P2 — UI/UX + minor correctness (fixed)

- **P2-1** Manual-game `ComboBox` and running-process suspend `CheckBox` lacked
  `AutomationProperties.Name`; added (`ManualGameLabel`, row `ProcessName`).
- **P2-2** Dead inner placeholder Border inside the collapsed Profiles editor
  panel removed (content unreachable; `FirstProfileHintText` still live at the
  list empty-state, `ProfilesGrid` references intact).
- Hardware sensor cards staying 2-column at narrow width: noted, NOT changed
  (needs responsive-layout logic; risk outweighs benefit in this pass).

### P3 — verified healthy, no change

Shutdown budget path, Session WAL/recovery, Riot blocks, Companion scopes/DTOs,
`ConfigureAwait`/lock discipline, benchmark arbitration, `innerHTML` (static
clears + localized strings via safe APIs; game names use `textContent`).

## 3. Fixes actually implemented

12 code files + 3 XAML + 1 JS + 1 new helper + 7 new tests. `git diff --stat`:
16 files, +218/−130. No new timers, no new services, no interval changes, no
`GC.Collect`, no working-set trimming, no AppId/version/updater changes.

## 4. RAM BEFORE / AFTER (isolated, Dashboard idle)

| Scenario | BEFORE WS / Priv MB | AFTER WS / Priv MB |
|---|---|---|
| Startup+25s | 190.3 / 91.3 | 184.2 / 89.2 |
| Idle+90s | 191.4 / 92.0 | 185.0 / 89.8 |
| Idle+330s | 188.9 / 90.4 | 189.3 / 89.9 |
| GC heap @idle | 6.37 MB / 83,110 obj | 6.31 MB / 83,099 obj |

Idle delta is noise (±3%) — expected (fixes target interaction churn, not the
baseline). Decomposition: GC heap ~7% of Private Bytes; native+WPF ~84 MB;
shared framework mappings ~95-99 MB. Flat over 5.5 min, handles/threads decrease:
no idle leak signal. Raw notes: `artifacts/diagnostics/ram-2026-11/` (git-ignored).

## 5. On the reported ~500 MB

Not reproduced in isolated idle (max WS observed: 191 MB). The idle baseline is
dominated by fixed costs (.NET/WPF, shared framework, GDI/driver mappings), not
by managed growth — GC heap is ~6.3 MB. Plausible real-use contributors (static
evidence only): per-evaluation icon decoding across three game lists (fixed),
unbounded JS blob-URL cache (fixed), unbounded throttle dictionary (fixed),
benchmark raw-frame lists retained per history selection (by design — transient
LOH during detail view, overwritten on next selection), active hardware/PresentMon
loops, larger real library/history. No RAM limit is claimed; no cosmetic memory
tricks were added.

## 6. Test results

- Release build: 0 warnings, 0 errors.
- Desktop suite: **863/863** (856 baseline + 7 new `LifecycleDetachTests`).
- Companion suite: **148/148**.
- `node --check` on all `wwwroot/js/*.js`: pass. `git diff --check`: clean.
- W1/W2 regression: `LibraryW1FixTests`, `Ui30BoundedRefinementTests`,
  `ActivityLocalizationTests`, `W2-LOCALIZATION-AUDIT.md` scope — all pass,
  untouched by this change (no maximize-hook, theme, or dictionary changes).
- Profiles save/load, shutdown-budget, tray tests: pass (in suite).
- NOT RUN: tab-by-tab RAM navigation (no UI automation harness), real-exit
  resource release (Close goes to tray by design — `CloseToTray` default true;
  tray instance terminated after confirmation), install test of the new setup
  exe (artifact verified by hash/metadata only), hardware-sensor-on memory
  profile (opt-in backend, off by default).

## 7. UI/UX check

W1 maximize hook (`WM_GETMINMAXINFO`) present and untouched. All lists now
virtualized with Recycling. Narrow-window stacking remains code-behind driven
(verified pattern, unchanged). Profile save reachable after selection on all
three editors (verified in XAML). PL/EN parity untouched (one hardcoded "FPS"
unit string is by-design, same in both languages).

## 8. W1 + W2 preservation

`main` was fast-forwarded to `origin/main` (`a7d16cc`, PR #23) before branching;
this branch adds only. Maximize fix, W1 library/session/integration changes and
W2 usability/localization deltas are all present in the diff base; full suites
covering them pass.

## 9. Integration (finalized post-merge)

- PR: TBD
- GitHub `main` SHA: TBD
- Local `main` SHA: TBD (must match)
- Worktrees/branches removed or kept: TBD
- Installer `artifacts/installer/FrameHub-Setup-1.0.0.exe`: TBD (size + SHA-256)
