# FrameHub performance + first-run phase 2026-11

Branch: `feature/first-run-perf` → `main`. Baseline `main` = `54a8a96`.
Machine: Windows 10/11 x64, 8 logical processors (4P+4T), .NET 10.0.402, Release.
PresentMon: **not installed** on this machine (proven §7) — live-capture scenarios NOT RUN.

## 1. Methodology

- Release `FrameHub.App.exe`, no debugger, isolated `FRAMEHUB_DATA_DIR` /
  `FRAMEHUB_LOCAL_DATA_DIR` temp roots per scenario (synthetic data only).
- CPU% = `TotalProcessorTime` deltas / wall / 8 cores (system-normalized %).
  Medians over 5–10 s samples; JIT warm-up excluded by leading stabilization.
- Memory: WorkingSet64, PrivateMemorySize64, Private Working Set (perf counter),
  threads, handles; GC heap via `dotnet-gcdump` (spot checks).
- Navigation driven for real via UI Automation (sidebar RadioButtons,
  SelectionItemPattern; verified on-screen per page).
- BEFORE binary = `main` @ `54a8a96` (idle soaks); interaction scenarios ran on
  the feature binary with all optionals OFF, which is code-identical on those
  paths (only dormant additions: onboarding marker write, live-gating branch).

## 2. Module cost table (measured + code evidence)

| Module | Default | Services/timers when on | Idle CPU (meas.) | RAM note |
|---|---|---|---|---|
| Core + profile watcher (always on) | ON, 2 s targeted name queries | `AppRuntimeService` watcher, no full enumeration | ~0% (Dashboard idle med 0%) | baseline WS ~184 MB, Priv ~91 MB |
| Processes page | on-demand (page active) | 2 s full enumeration + CPU deltas | only while open | — (see nav) |
| Session auto-detect | OFF unless Auto mode | 3 s batch observation | 0 when off | — |
| ActiveGameMonitor | on with Companion | 2 s shared-observation scan | part of Companion cost | — |
| Hardware/LHM sensors | OFF (wizard + Settings) | lease + 1 s refresh | ~0% here (no admin; load sensors cheap) | WS 187.6 vs 183.7 idle |
| Live PresentMon loop | OFF (new gate) | 250 ms native loop | dormant only (no session w/o game) | — |
| Benchmark capture | on demand | 20 ms loop during capture | 0 when idle | transient LOH per capture |
| Companion/Kestrel | OFF | listener + 500 ms publisher + 2 s ActiveGame | +0.37% CPU, +~22 MB WS, +9 threads | WS 206 vs 184 idle |
| Loopback REST client | — | per-request | +~0% over Companion baseline | — |
| Update check | one-shot on visible start | single HTTPS check | negligible | — |
| Logging | rolling file | append | negligible | — |
| 9 eager VMs | always composed | none (no timers at rest) | 0 | kept eager: navigation must stay instant; composition cost is one-shot at startup (see start times) |

## 3. BEFORE/AFTER results

Cold start (input-idle): BEFORE 5409 ms / AFTER 804 ms / warm BEFORE 802 ms.
(Cold includes first-JIT + service discovery; onboarding marker write adds nothing measurable.)

| Scenario (isolated data, medians) | BEFORE CPU% | BEFORE WS / Priv MB | AFTER CPU% | AFTER WS / Priv MB |
|---|---|---|---|---|
| Dashboard idle 11–12 min, window visible | 0 (max 1.52) | 183.7 / 91.1 | 0 (max 0.76) | 196.6 / 96.7¹ |
| Dashboard idle, no-wizard control | — | — | 0.02 (max 0.53) | 184.2 / 88.6 |
| Tray (Hide-to-tray) 12 min | 0 (max 0.39) | 191.5 / 91.8 | 0 (max 0.16) | 184.7 / 88.3 |
| Intensive nav 18×9 screens (200-item lib, 40-session history) | INVALID² | INVALID² | 0.48 (max 3.86) | 266 / 140³ |
| Companion ON + live dormant, no clients | 0.37 (max 0.66) | 206.3 / 96.4 | 0.45 (max 0.66) | 206.0 / 96.1 |
| Companion ON + loopback REST polling | 0.39 (max 1.40) | 207.7 / 96.5 | 0.47 (max 1.37) | 207.5 / 96.3 |
| Hardware page ON, sensors open, 1 s refresh | UNVERIFIED⁴ | UNVERIFIED⁴ | 0.08 (max 0.35) | 218.2 / 103.2 |
| Full exit | process gone; no PresentMon service exists | — | TBD | — |

² BEFORE nav runs used SelectionItemPattern-only UIA automation, which only checks
the sidebar radio without firing NavigateCommand — proven post-hoc via IsOffscreen
materialization tests. Those numbers measured Dashboard idle, not navigation, and
are discarded (CSVs kept, marked invalid). AFTER nav uses Focus+Space keyboard
input (fires Click → NavigateCommand), verified per page by onscreen markers.

³ AFTER nav climbs 217→272 MB across the first full pass (page trees materialize,
icon cache fills to its 96-cap, 200 library VMs + 40 history items retained by
design), then saturates (+~6 MB on second pass) and holds flat at 266.8 MB with
exactly 0% CPU once back on Dashboard — bounded retention, not a leak. GC heap
post-nav 11.5 MB (idle 6.4 MB); the rest is native WPF/D3D resources. Page timers
(Processes 2 s, Benchmark detector) provably stop: CPU returns to 0.00%.

⁴ BEFORE hardware runs used the same Select-only automation: page display unproven,
so those numbers are discarded as page-cost evidence. AFTER hardware cost rests on
the keyboard-verified run (page proven onscreen) plus the lease-gated backend design.

¹ AFTER idle-with-wizard run includes one wizard open/skip cycle: +~13 MB one-time
(JIT + dialog resources on a once-per-install path). The no-wizard control matches
BEFORE within noise. GC heap AFTER: 6.45 MB / 84k obj (BEFORE 6.37 MB) — no leak.

## 4. Optimizations shipped

1. **Live FPS gate** (`LiveTelemetryEnabled`, default OFF): `LivePerformanceTelemetryService`
   loop starts only with Companion ON + live ON. ActiveGame discovery untouched
   (Session/phone depend on it). Runtime toggle, no restart.
2. **First-run wizard**: fresh installs get 6-step dark UI (PL/EN live switch),
   optionals default OFF; upgrades never see it (file-existence + marker state
   machine, interruption-resuming).
3. **Settings → Live FPS toggle + Data & reset category**: settings restore
   (keeps library/profiles/history/pairings) and typed-RESET full wipe
   (allow-listed roots only, blocked mid-benchmark/session, ends in restart →
   wizard). Reuses store owners (`SessionStateService.Clear`,
   `DeviceRecordStore.ResetStore`); active log truncated (handle held).
4. No new timers/loops (BACKGROUND-WORK.md start-condition note only), no
   GC.Collect/trimming, no interval inflation.

## 5. PresentMon service findings (§7 detail)

- `PresentMonSharedService`: NOT installed here (`sc` 1060, no HKLM service key,
  no `%ProgramFiles%\Intel` dir, no API DLL).
- Bundled Intel MSI (v2.5.1, cached prerequisite) declares
  `ServiceInstall PresentMonSharedService StartType=2 (Automatic)` — read from
  the MSI table without installing. So on installed machines it auto-starts at
  boot via SCM; FrameHub never changes StartMode (installer has no service
  stanza; app has zero ServiceController code — verified by grep).
- FrameHub only `pmOpenSession`s; requires a running service, never starts it.
- On cooperative exit all FrameHub-owned sessions close (loop finally +
  capture finally + 8 s shutdown budget); the Windows service is intentionally
  left alone (shared component; uninstall never removes it per BENCHMARKING.md).
- Live FPS OFF ⇒ no FrameHub native sessions (dormant 250 ms checks only).
- Service own CPU/RAM: NOT RUN (not installed; installing a system service is
  out of scope and would change machine state).

## 6. ~500 MB reproduction

NOT reproduced: max WS observed ~208 MB (intensive 27-tab nav, 200-item library,
40-session history). Idle 184–191 MB; tray 191 MB (Hide-to-tray does NOT trim
like minimize does — RUNTIME-OVERHEAD's 99 MB was minimize-to-taskbar; worth
noting, not a bug).

## 7. Tests & validation

- Release 0w/0e; Desktop 878/878 (+15 new); Companion 148/148; JS intact;
  `git diff --check` clean.
- Interactive UIA verification: wizard 6 steps EN→PL, toggles, Finish/Skip
  persisted correctly; 27/27 tab navigations; Skip path settings verified.
- NOT RUN: live PresentMon ON (no service), install-execution test, screen
  reader, DPI text-fit beyond resizable window + scroll, long real-game session.
