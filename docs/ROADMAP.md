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

## 24.10 — modernizacja po Etapie 0 (zaakceptowana 2026-10-24, `main` = `f3114fe`)

Zakres: PR #7 (`coder/t_f091ff98-24-10` @ `205c265` → merge `f3114fe`), 17 commitów, 26 plików, +2662/−73 od bazy `a175a25`. CI exact-head 3× success. Walidacja AGENTS na scaleniu: build 0/0, Desktop 700/700, Companion 140/140, JS 7/7. F-DOC-1…8 z review Etapu 0 pozostają naprawione (13 scopes, 17 wierszy, 9× `innerHTML` nadal celowo, `BENCHMARKING.md:95`, `~1800` linii, flagi `_isLocallyAwaitingCapture`/`_localTerminalApplied`); nowa dokumentacja nie zmienia tych liczb.

### Wdrożono (potwierdzone kodem + testami + niezależnym QA, werdykt PASS WITH WARNINGS)
- Stabilność P0/P1: marshal `SessionStateChanged` na dispatcher UI (`SessionStateChangedMarshalingTests`, deterministyczne STA red/green); budżetowany shutdown `MainWindow → Shell.ShutdownAsync(budget) → AppRuntimeService.ShutdownAsync` (koperta 8 s, timeout = jawny błąd, nie blind kill; `ShutdownBudgetTests`); serializacja `ProcessesViewModel.RefreshAsync` + lokalny `CancellationToken` (QA-P2-3, RED 2/2 `ObjectDisposedException` → GREEN); dispose-before-replace rotowanych CTS; serializacja `ComparisonRebuild` (`_comparisonGate`); `TryResume` przez wstrzykiwane delegaty.
- Restart kooperacyjny (dyrektywa §B, R1): `SettingsService.TryStartSuccessorProcess` — tylko zweryfikowany `_exePath`, whitelist argv (`--tray`, `--minimized`), single-flight, successor-first, timeout = jawny błąd + `ResetRestartState`; wyzwalany z tray-menu (`MainWindow.RestartApplication`); 5 testów w pliku `RestartSuccessorTests` (VERIFIED `grep -c TestMethod` = 5) + filtr 13/13 (Restart 4 + Tray 9, REPORTED `FH-24-10-CODER-EVIDENCE.md:30`, skład filtra niezweryfikowany niezależnie). E2E restartu przez tray = NOT RUN (brak automatyzacji zasobnika).
- DNS diagnostyka Faza 1 (dyrektywa §C, R2-F1): `DnsDiagnosticsService` **read-only, library-only** — enumeracja adapterów, serwery per-interface (DHCP/statyczne), latencja per-resolver, podgląd komend; celowo ZERO API mutacji; brak konsumentów produkcyjnych (Desktop UI odłożone). 8 testów. Mutacja systemowa (Faza 2) NIE istnieje — jawnie odłożona do czasu udowodnienia rollbacku.
- Premium PAKIET A1 (dyrektywa §D, R3-A1): eksport porównania benchmarków do CSV/Markdown (`BenchmarkComparisonExportService`, statyczny konsument `BenchmarkStorageService` + `BenchmarkComparisonService`, bez zmian matematyki; determinizm bajt-identyczny, daty UTC ISO-8601); wpięty w zakładkę Compare (WPF `SaveFileDialog` z ownerem). W pełni addytywny: zero gatingu istniejących funkcji, zero licencji/DRM/paywalla/serwera. 8 testów + golden.
- F-2 izolacja danych (QA-P2-2): `AppPaths` honoruje `FRAMEHUB_DATA_DIR` / `FRAMEHUB_LOCAL_DATA_DIR` (override katalogu danych użytkownika/lokalnego + migracja legacy); 5 testów. Domyślna ścieżka bez zmian (`%APPDATA%\FrameHub`); pomiary QA wykonywano w izolowanym katalogu.
- Pomiary na HEAD (QA, Release, 2026-10-24): warm start 1224 ms (bramka ≤1522 ms); WM_CLOSE→exit 128–134 ms, kod 0; soak 300 s WS +1,2%, uchwyty 756→708, idle CPU 0,002–0,065% (bramka ≤0,12%). Baseline Etapu 0 (`a175a25`, 2026-10-24): warm 1303/1384 ms, pierwszy po buildzie 4224 ms, soak 1800 s WS 210,3 MB / Private 102,5 MB / CPU 0,102%, REST p50 2,09 ms / p95 7,95 ms, UI p50 4,6–5,8 ms. NOC (2026-10-24, `t_602bd1b8`) ma już tylko wartość historyczną. Kadencje chronione bez zmian (200 ms progress, 250 ms live, 500 ms WS, 1 s poll, 3 s auto, 4 s session-poll).

### Jawnie odłożone (decyzje z dyrektywy, nie luki implementacyjne)
- HUD: 0 kodu (warunek antycheat niespełnialny do udowodnienia) — WSTRZYMANE.
- WinUI 3 / migracja UI: odłożone z powodem (brak dowodu zysku; WPF wspierany w .NET 10) — bez PoC w tej fazie.
- Auto-updater/hosting/backend aktualizacji: PÓŹNIEJ (tylko check `version.txt`).
- Zdalny serwer/VPS/public API/przebudowa Companion: PÓŹNIEJ.
- Mutacja DNS (Faza 2) i DNS-UI: odłożone (warunek: udowodniony rollback + akceptacja UX).
- `LibraryViewModel` split (1837 linii), `GameData` adopt/remove, trendy benchmarków (A2), reguły Session per-gra: odłożone z powodem (wpisane w REFACTOR-CANDIDATES / Experimental).
- `Module.Toolkit.*` (20 kluczy lokalizacyjnych): rezerwacja pod ewentualny moduł Toolkit, nie ślad usuniętego kodu; decyzja Toolkit TAK/NIE wisi.

### NOT RUN na HEAD (przyczyny w raporcie QA; status niezmieniony przez ten zakres)
Capture e2e/live telemetry/preempcja (brak PresentMon i gry), sensory i lease Hardware, nawigacja/render/scroll, tray→Exit i pełny budżet 8 s wall-clock, parowanie Companion/scopes 403/WS z biletem, GC/alloc, DNS/registry/schtasks/restart-admin/mutacje procesów. Otwarte: QA-P2-1 (test regresji `ComparisonRebuild` nie czerwienieje na pre-fix — brak niezależnego dowodu red/green dla fixu wyścigu z CI).

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
