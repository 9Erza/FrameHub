# FrameHub User Guide

[Polski](USER_GUIDE.pl.md)

On first launch, scan **Game Library**. Add custom folders or a manual executable if a launcher scan misses a game. Select an item to save its CPU profile; **Save** persists a rule, while **Optimize/Apply** attempts to change currently running matching processes.

Use **Session Optimization** to choose what may be suspended, preview the result, then run an automatic or manual session. FrameHub records suspended processes and resumes them during recovery; it does not kill them.

**Processes & CPU** is for immediate running-process control. **Profiles & Rules** manages saved rules and the watcher. Profiles with executable paths only target that normalized path; legacy profiles without one use name matching.

For CS2, close the game before scanning, editing, or restoring config. FrameHub creates backups and will not silently choose among ambiguous Steam userdata accounts. Hardware Monitor is session-only and must be enabled explicitly. Settings includes tray, language, logs, startup, and optional elevation. Data and logs are under `%APPDATA%\FrameHub`.

Troubleshooting: refresh the library/process list, verify the executable path, close CS2 before config edits, and use Logs for failure details. Protected processes can require administrator permission.

When CS2 config is detected in exactly one Steam userdata folder, FrameHub uses it automatically. If several valid folders exist, choose the numeric Steam userdata ID in the CS2 detail view. Until then, FrameHub blocks editable config reads and all CS2 write actions. New backups are kept separate for each resolved userdata path.

## Benchmarks (v0.6.0)

1. Add or scan the game in **Game Library**.
2. Start the game.
3. Open **Benchmarks**, or select the game and press **Benchmark** in its Library detail.
4. Select 30, 60, 120 seconds or a custom 10–600 second duration, plus an optional countdown.
5. Start the test, return focus to the game, and reproduce the intended scene/workload.
6. Review Average FPS, 1% Low, 0.1% Low, P95/P99 frame time, quality warnings and the frame-time graph.
7. Repeat under the same game, graphics, scene and system conditions.
8. Use **Compare** for two completed sessions of that game.

The global benchmark hotkey is unassigned and disabled by default. Configure it under **Settings > Benchmark hotkey** by selecting **Change / record shortcut**, then pressing a supported modifier combination (or F8–F12). The same shortcut starts and stops capture while FrameHub is focused, minimized, in the tray, or while the game has focus. A hotkey start is immediate; the normal Start button still uses the configured countdown, and the saved countdown is not changed. FrameHub implements this with the Windows `RegisterHotKey` API and does not install a keyboard hook.

The hotkey uses the selected running library game. If none is selected, FrameHub proceeds only when exactly one benchmarkable library game is running. When multiple games make the target ambiguous, it logs the event and asks you to select the game in FrameHub instead of guessing.

Average FPS describes throughput; 1%/0.1% Low and high frame-time percentiles help expose intermittent stutter and pacing problems. They do not identify the cause by themselves. Capture records the current CPU profile and Session Optimization state but never changes either one. **History** includes local WPF and developer-harness schema-v1 sessions, can open their folder, and can permanently delete one validated session.

Data remains under `%LOCALAPPDATA%\FrameHub\Benchmarks`; settings and logs remain under `%APPDATA%\FrameHub`. No benchmark data is uploaded. If the benchmark engine is unavailable, repair/reinstall FrameHub—the single Setup contains the pinned PresentMon prerequisite. FrameHub itself does not inject into games or read game memory; compatibility with a particular game or anti-cheat may vary.
