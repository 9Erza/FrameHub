# Roadmap

## Implemented

- Game library scanning, CPU profiles, profile watcher, Session Optimization, CS2 configuration workflow, optional hardware monitoring, PL/EN UI, logs, tray behavior, and Windows startup configuration.
- FrameHub v0.6.0 PresentMon Service/API capture and packaging, exact-PID automatic game detection, global benchmark hotkey, WPF Capture/History/Compare workflow, frame-time graph, context metadata and local session management.
- FrameHub v0.6.0 dark-slate UI redesign with consistent shared controls and simplified typography.
- M10.1 Companion control for explicitly opted-in trusted BackgroundApp Library items, with dedicated read/write scopes, benchmark interlocks, and process identity validation.
- M10.2 Gaming Quick Actions / Gaming Mode: a Dashboard quick action that composes trusted Library launch, the shared launch cooldown, and Session Optimization lifecycle (optimize-only when the game is already running), with live session state and Restore.
- Riot Games discovery (League of Legends, VALORANT): passive Start Menu shortcut discovery, official-shortcut launch, actual-game process identity, and hard non-mutation/non-benchmark protection for Riot game/client/Vanguard processes.
- Architecture/performance consolidation checkpoint: shared on-demand process observation, batch Library/Background state, benchmark-safe profile mutation, unified Session preview policy, separated Companion providers, modular frontend, and canonical architecture documentation.
- Benchmark Environment Metadata v1: one-shot, best-effort, backward-compatible environment context (OS/build, CPU, GPU/driver, RAM, primary display, FrameHub version) captured per benchmark, shown in Desktop benchmark details, and surfaced as advisory environment differences during comparison.
- Companion Game CPU Assignment V1 & UX Polish: active-game temporary CPU scheduling override (Affinity / CPU Sets), separate Session Optimization (background apps) and Game CPU Assignment cards, CPU Sets recommendation, topology-driven presets (All, Physical only, Clear), and dedicated scopes (`read:optimization-cpu`, `write:optimization-cpu`).
- Final UX Cohesion & Release Polish: Companion game icons with client-side blob caching, official branding in Companion header, Missing Executable handling across Desktop and Companion with safe disabled actions, automated removal of leaked test artifacts, Desktop "Games & Optimization" ("Gry i optymalizacja") navigation clarity, ComboBox display fix, "Quick Start" ("Szybki start") card clarity, and streamlined Settings.
- Runtime Resource & Overhead Audit: comprehensive background work inventory, memory working set characterization and stabilization analysis, 10-minute idle soak verification showing stable plateau (<0.15 MB/min slope, ~0.25% idle CPU), verified lease-controlled hardware monitor shutdown and zero duplicate background loops.
- FrameHub v0.7.1 Mobile Companion Polish & Update UX: desktop update notification reworked (once-per-process automatic check gated on real window presentation, silent no-update/error results, shared FrameHub-styled update dialog for automatic and manual checks); Companion browser identity (favicon, Apple touch icon, theme color from the official logo); Companion mobile app-shell rework (fixed header, single scrolling content region with footer, layout-anchored bottom navigation, iOS safe-area/`100dvh` handling, root horizontal-overflow prevention, compact phone density, inline SVG navigation icons, user-facing footer copy).

## 24.10 — post-Stage-0 modernization (accepted 2026-10-24, `main` = `f3114fe`)

Scope: PR #7 (`coder/t_f091ff98-24-10` @ `205c265` → merge `f3114fe`), 17 commits, 26 files, +2662/−73 from base `a175a25`. CI exact-head 3× success. AGENTS validation on the merge: build 0/0, Desktop 700/700, Companion 140/140, JS 7/7. F-DOC-1…8 from the Stage-0 review stay fixed (13 scopes, 17 rows, 9× `innerHTML` still intentional, `BENCHMARKING.md:95`, `~1800` lines, `_isLocallyAwaitingCapture`/`_localTerminalApplied` flags); the new documentation does not change these numbers.

### Implemented (verified by code + tests + independent QA, verdict PASS WITH WARNINGS)
- P0/P1 stability: `SessionStateChanged` marshaled to the UI dispatcher (`SessionStateChangedMarshalingTests`, deterministic STA red/green); budgeted shutdown `MainWindow → Shell.ShutdownAsync(budget) → AppRuntimeService.ShutdownAsync` (8 s envelope, timeout = explicit error, no blind kill; `ShutdownBudgetTests`); serialized `ProcessesViewModel.RefreshAsync` + local `CancellationToken` (QA-P2-3, RED 2/2 `ObjectDisposedException` → GREEN); dispose-before-replace of rotated CTS; serialized `ComparisonRebuild` (`_comparisonGate`); `TryResume` via injected delegates.
- Cooperative restart (directive §B, R1): `SettingsService.TryStartSuccessorProcess` — verified `_exePath` only, argv whitelist (`--tray`, `--minimized`), single-flight, successor-first, timeout = explicit error + `ResetRestartState`; triggered from the tray menu (`MainWindow.RestartApplication`); 5 tests in `RestartSuccessorTests` (VERIFIED `grep -c TestMethod` = 5) + 13/13 filter (Restart 4 + Tray 9, REPORTED `FH-24-10-CODER-EVIDENCE.md:30`, filter composition not independently verified). Tray-triggered restart E2E = NOT RUN (no tray automation).
- DNS diagnostics Phase 1 (directive §C, R2-F1): `DnsDiagnosticsService` **read-only, library-only** — adapter enumeration, per-interface servers (DHCP/static), per-resolver latency, command previews; intentionally ZERO mutation API; no production consumers (Desktop UI deferred). 8 tests. System mutation (Phase 2) does NOT exist — explicitly deferred until rollback is proven.
- Premium PACKAGE A1 (directive §D, R3-A1): benchmark-comparison export to CSV/Markdown (`BenchmarkComparisonExportService`, static consumer of `BenchmarkStorageService` + `BenchmarkComparisonService`, no math changes; byte-identical determinism, UTC ISO-8601 dates); wired into the Compare tab (WPF `SaveFileDialog` with owner). Fully additive: zero gating of existing features, zero license/DRM/paywall/server. 8 tests + golden.
- F-2 data isolation (QA-P2-2): `AppPaths` honors `FRAMEHUB_DATA_DIR` / `FRAMEHUB_LOCAL_DATA_DIR` (user/local data directory override + legacy migration); 5 tests. Default path unchanged (`%APPDATA%\FrameHub`); QA measurements ran in an isolated directory.
- Measurements on HEAD (QA, Release, 2026-10-24): warm start 1224 ms (gate ≤1522 ms); WM_CLOSE→exit 128–134 ms, code 0; 300 s soak WS +1.2%, handles 756→708, idle CPU 0.002–0.065% (gate ≤0.12%). Stage-0 baseline (`a175a25`, 2026-10-24): warm 1303/1384 ms, first-after-build 4224 ms, 1800 s soak WS 210.3 MB / Private 102.5 MB / CPU 0.102%, REST p50 2.09 ms / p95 7.95 ms, UI p50 4.6–5.8 ms. NOC (2026-10-24, `t_602bd1b8`) now has historical value only. Protected cadences unchanged (200 ms progress, 250 ms live, 500 ms WS, 1 s poll, 3 s auto, 4 s session-poll).

### Explicitly deferred (directive decisions, not implementation gaps)
- HUD: 0 code (anti-cheat condition cannot be proven) — SUSPENDED.
- WinUI 3 / UI migration: deferred with reason (no proven benefit; WPF supported on .NET 10) — no PoC in this phase.
- Auto-updater/hosting/update backend: LATER (only `version.txt` check).
- Remote server/VPS/public API/Companion rebuild: LATER.
- DNS mutation (Phase 2) and DNS-UI: deferred (condition: proven rollback + UX acceptance). NOTE: the read-only DNS *diagnostics UI* shipped later in UI overhaul C (Settings > Companion & Network); only *mutation* stays deferred.
- `LibraryViewModel` split (1837 lines), `GameData` adopt/remove, benchmark trends (A2), per-game Session rules: deferred with reason (listed in REFACTOR-CANDIDATES / Experimental).
- `Module.Toolkit.*` (20 localization keys): reservation for a possible Toolkit module, not a trace of deleted code; Toolkit YES/NO decision pending.

### NOT RUN on HEAD (causes in the QA report; unchanged by this scope)
E2E capture/live telemetry/preemption (no PresentMon or game), Hardware sensors and lease, navigation/render/scroll, tray→Exit and full 8 s wall-clock budget, Companion pairing/scopes 403/WS with ticket, GC/alloc, DNS/registry/schtasks/restart-admin/process mutations. Open: QA-P2-1 (`ComparisonRebuild` regression test does not go red on pre-fix — no independent red/green proof for the CI race fix).

## UI overhaul B+C — Ember Signal redesign (accepted 2026-10-25, `main` = `25c3419`)

Scope: PR #10 UI-B (`coder/t_aa6bb97a-ui-b` @ `fd47b39` → merge `221da77`, 24 files, +1586/−149) and
PR #11 UI-C (`coder/t_6b4535fd-ui-c` @ `051bd44` + CI-only `defa21a` → merge `25c3419`,
42 files, +4361/−91 vs `221da77`; `defa21a` differs from `051bd44` only by `fetch-depth: 0`
in `ci.yml` + `supporter-compat.yml`, no UI/test/tree changes). Human decision OPCJA A kept
(`051bd44` approved; QA R5 PASS WITH WARNINGS, 6× P3, no R6 follow-up fixes).
Post-merge validation on `25c3419` (AGENTS): build 0 warnings / 0 errors, Desktop 775/775,
Companion 141/141, JS 7/7, `git diff --check` clean. Post-merge CI on `main`: 2/2 success.
No open PRs. All 15 tracked refs verified IN-MAIN (`merge-base --is-ancestor`, uniq=0);
history preserved, nothing deleted.

### Implemented (verified by code + tests + independent QA)
- Ember Signal design system: ~10 tokens in `Colors.xaml` (background `#121419`, text `#F7F4EC`,
  primary accent amber `#F2A93B`, CTA contrast 9.3:1), flat brushes, radius ≤10, no blur/shadow/
  looped animations; `AllowsTransparency=False` kept; `Themes/HighContrast.xaml` + 8 `App.xaml.cs`
  references; 200%-DPI/min-window render probed (one P3: dashboard tile titles clip without
  ellipsis at 685×400 DIP).
- Shell + navigation: 42 px titlebar, 224 px rail with main/games/performance/system groups,
  two-pill status strip (Monitor / Session), window footer (session · monitor · PresentMon ·
  `MIT · v0.7.1`), rail footer keeps `9Erza` · `DobryPC.pl` · `Wesprzyj FrameHub` · About/licenses.
- 9 views rebuilt from E01–E10: Dashboard (hero + 4 clickable metric cards + Top-CPU preview +
  last-3 activity), Library (single primary split-scan button, sources segment, X/Y ready bar,
  search/filter, virtualized rows), Session (4-step wizard + sticky Restore), Benchmarks
  (game → engine-cause state machine → configure → start; History/Compare, export via dialog
  with write-error handling), Processes (7 columns + rule column, sort/filter, right detail
  with topology presets, virtualization kept), Profiles (creator-first, 0–50 rows
  with search/toggle/conflicts/validation), Hardware (OFF/ON-lease/UNAVAILABLE states,
  storage opt-in `EnableStorageSensors`, read-only timer-resolution card), Logs (severity
  filter + search, visible 200/N cap, confirmed Clear), Settings (6 subnav categories, per-category
  visibility incl. the DNS panel, normal + admin restart with feedback).
- Localization: Desktop dictionary 1005/1005 PL/EN keys both directions, 0 duplicates/empties/
  key-echoes (QA R5); Companion `i18n.js` parity suite; language switch persists to
  `settings.json` (`Language: pl|en`, default `en`); UI culture formats, export stays
  InvariantCulture/UTC. Canonical terms in [Glossary](architecture/GLOSSARY.md)
  (Dashboard = Panel główny, Logs = Dziennik — decided, not proposed).
- Quality-of-life: read-only DNS panel (adapters, DHCP/static resolvers, Cloudflare/Google/
  Quad9/OpenDNS + IPv4/IPv6/DoH latency comparison, explicit read-only notice, zero mutation
  code — grep-verified), cooperative restart feedback (success/timeout/UAC-refusal),
  public CSV/Markdown export (deterministic, no gating), empty/loading/error/permission states
  per view, keyboard-only operability with visible focus and `AutomationProperties`.
- Supporter PoC (no private repo, no accounts, no payments — see [Edition integration](../supporter/EDITION-INTEGRATION.md)):
  manifest `supporter/edition.manifest.json` (pinned SHA `221da77`), `supporter-compat` CI job,
  `SupporterEditionTests` (separation + adoption), About/Support UX. Blocker: adopting the new
  `main` pin (`25c3419`) requires explicit owner consent + green compat suite.
- Unchanged domain invariants (no new owners): `ProcessService`, `SessionOptimizationCoordinator`,
  `BenchmarkCaptureCoordinator`, `LivePerformanceTelemetryService`, `ProcessSuspendService`,
  `ProcessScannerService` (sole CPU sampler), `AppRuntimeService`, `HardwareMonitorService`;
  protected cadences untouched (benchmark progress 200 ms, live 250 ms, WS 500 ms, active game
  2 s, session auto 3 s, session poll 4 s, capture 20 ms).

### Proposed (not implemented, no code)
- Private `FrameHub-Supporter` repo with extension-only modules (BenchmarkLab, history
  dashboards, preset versioning, per-game automations, layout personalization): requires explicit
  user consent; start without server, accounts, or paid integrations.
- Manifest pin adoption `221da77` → `25c3419` after a green `SupporterEdition` compat run.
- Support platforms evaluation (no integration): Buy Me a Coffee first (5% platform + ~2.9%+$0.30
  Stripe + 0.5% payout, weekly/monthly; sources 2026-10-24), Patreon (10% for new creators after
  2026-08-04, PLN processing/payout fees; sources 2026-10-24), GitHub Sponsors terms NOT verified
  (retrieval failed — explicit blocker, no rates quoted from memory). Tax/formalities need an
  accountant; out of scope for this round.
- Toolkit YES/NO, `GameData` adopt/remove, benchmark trends (A2), per-game Session rules,
  DNS mutation Phase 2, light theme, WinUI 3, HUD, updater backend, VPS work.

### NOT RUN (environmental, with reasons — not hidden gaps)
- UIA live scroll p50/p95 (ScrollPattern invalid-state; offscreen equivalent measured: grid
  p50 41.4 / p95 50.2 ms).
- Whole-process GC generations (no `dotnet-counters`/EventPipe in the environment; UI-load GC
  measured instead: +12/+4/+2 gen, 36.3 MB alloc, 9.1 MB heap).
- System DPI change 125/150/200% (no OS-setting authorization; DIP-equivalent + render probed).
- Private Supporter repo (needs explicit consent).
- Live pixel screenshot EN (operator foreground locked; covered by 10 offscreen EN PNG +
  EN runtime log). Measured instead: soak 1800 s (WS −6.3 MB, private +0.04 MB, handles −19.7,
  no rising trend), UIA navigation p50 104.6 / p95 267.2 ms, live 1920×1032 capture.

## Planned Near-Term Backlog

> NOC 2026-10-24 annotations (branch `noc/t_2e7bd27e-0c6efc2`, QA PASS WITH WARNINGS —
> plans, not implementations; details in `docs/audits/`):
> - **Before merge:** harden `LateCapturingSnapshot_…` with a positive injection control
>   (open P2: possible vacuous PASS depending on test order). Acceptance: 10 consecutive
>   full-suite runs green incl. first run after build.
> - **Etap-0 2026-10-24 (`t_eb7d4587`, commit `f53e91e`): P2 FIXED** — positive injection
>   control + `action()` fallback in `DispatchIfRequired` (dead/shutdown dispatcher
>   applies inline instead of dropping); new `InOrderWaitingSnapshot_…` proves
>   countdown/progress stays visible during local await. Negative controls on `e70e967`
>   FAIL as expected (`Expected <Failed>, Actual <Capturing>`; `Expected <Waiting>,
>   Actual <Idle>`). Merge gate now depends only on independent same-card QA of `t_eb7d4587`.
> - **PresentMon v2.6.0 pilot (gated, not scheduled):** only after ABI validation
>   (`PresentMonIntrospectionTests` + harness `--presentmon-api-dll`), then pin + SHA-256
>   + QA regression. Native ETW backend rejected; static-link core research-only.
> - **GameData adopt-vs-remove** (P3, dormant confirmed, separate reviewed change).
> - **Follow-up cards (proposed):** data-directory isolation (`AppPaths.cs:10-13`, F-2),
>   JS logic-test harness (F-3), update-signature threat model, remaining lambda-subscription
>   leaks (`HardwareViewModel`, `ProfilesViewModel`, `ShellViewModel`, `MainWindow`).
> - NOC measurements: before-values in `docs/audits/2026-10-24-noc-performance.md`
>   (after = NOT RUN: handler-only change, no hot path + F-2 contamination risk).

1. **Session Optimization & Game CPU Assignment Follow-ups**:
   - Maintain clear separation between Session Optimization (background process suspension/recovery) and Game CPU Assignment (active game affinity / CPU Sets scheduling).
   - Continue promoting CPU Sets as recommended where supported by platform topology.

## Riot Games Support Boundaries

Riot support is deliberately conservative and is system/library management only:

- Discovery reads only official Riot-created Windows Start Menu shortcuts; no Riot metadata directories, lockfiles, client protocols, or Riot endpoints are used.
- Launch executes the official shortcut itself (ShellExecute, no FrameHub-supplied arguments); Riot game executables are never direct-launched.
- Riot game/client/Vanguard processes are never suspended, killed, reprioritized, pinned, or otherwise mutated; Session Optimization and Gaming Mode affect other background processes only.
- Riot games are excluded from benchmark capture and live PresentMon targets pending explicit Riot-specific review; they also do not appear as Companion "active game".
- Teamfight Tactics is not discovered because its game process identity collides with League of Legends.
- FrameHub is not Riot-approved; distributing a player-facing Riot-integrated product may require Riot Developer registration/policy review.

## Experimental / Research

- Benchmark comparison methodology and advanced PresentMon/frame-generation research beyond the v0.6.0 foundation.
- Additional Windows utilities subject to safety and reversibility review.
