# FrameHub NOC performance report (point-in-time, 2026-10-24)

Status: point-in-time. The canonical long-term record stays in
`docs/RUNTIME-OVERHEAD.md` (10-minute soak, ~310–330 MB active window,
~90–105 MB tray, ~0.23–0.25% idle CPU). This file records the NOC
**before** measurements, the conditions, and why there is no **after** measurement.

- Before baseline: `main` @ `0c6efc24`, REPORTED from baseline QA (`BASELINE-REPORT.md` + logs,
  SDK 10.0.402, runtime 10.0.13). Not re-run here.
- After: **NOT RUN** (see §3). The NOC code change touches two conditions in a UI event
  handler — no hot path, no allocation/timer/loop change — and any re-measurement would have
  been contaminated (F-2, see §2).

## 1. Before measurements (NOC baseline, 2026-10-24)

| Metric | Run 1 | Run 2 / soak |
|---|---|---|
| Start → visible window | 2812 ms | 1550 ms |
| Working Set, idle 30 s after start | 172.8 MB | 168.9 MB |
| Private Bytes, idle 30 s after start | 80.7 MB | 78.8 MB |
| 240 s idle soak, Working Set | — | 169.6 → 171.2 MB (flat) |
| 240 s idle soak, Private Bytes | — | 82.4 → 79.9 MB (flat/down) |
| 240 s idle soak, handles / threads | — | 697 → 706 / 26 → 14 |
| Idle CPU (15–240 s) | 0.00–0.03 % | 0.020–0.029 % → 0.000 % |

Verdict (quote exactly): **no leak evidence within 240 s; the horizon is too short
to claim absence of a leak.** Do not document this as "no leak".

## 2. Conditions (required for any comparison)

Built `FrameHub.App.exe` from `bin/Release`, no install; per-PID sampling;
`CloseToTray=true` ⇒ `WM_CLOSE` hides to tray **by design** (not a shutdown bug);
`CompanionEnabled=false`; `HardwareMonitorEnabled=false`; console session / ID 1;
`PresentMonSharedService` **not installed** (app log confirms supported degradation,
normal startup — capture e2e NOT RUN).
Contamination warning (F-2): close all other `dotnet`/`testhost` processes before measuring;
during the 240 s "idle" soak the shared `%APPDATA%\FrameHub\FrameHub.log` grew by 34 661 B
with entries from parallel tests (`FrameHub.Tests\Temp\…`, Companion ports from parallel runs).
Sampling protocol for a valid before/after: 3 runs, median, points 0/10/30/60/120/180/240 s,
same settings as above.

## 3. After: NOT RUN (cause)

- The NOC diff (`BenchmarkViewModel` stale-snapshot gates + `DashboardViewModel.Dispose`)
  changes UI-event-handler branching only; it adds no timer, loop, allocation pattern or
  sampling change, so no performance delta is expected and none was measured.
- Re-measuring on this machine would not be comparable (F-2 contamination, see §2).
- Independent QA likewise performed no CPU/RAM measurement on HEAD (`QA_REVIEW.md` §7 NOT RUN).

## 4. Relation to the existing 10-minute soak (`RUNTIME-OVERHEAD.md` §1–§2)

The two measurement sets differ in machine state and methodology and are **not directly
comparable** (different Working-Set levels: ~169–173 MB here vs ~328–329 MB there).
Both agree on the qualitative result: flat trend, no evidence of an idle runaway.
Do not present either historical set as a new result of this NOC.
