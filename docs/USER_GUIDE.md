# FrameHub User Guide

[Polski](USER_GUIDE.pl.md)

On first launch, scan **Games & Optimization** (Game Library). Add custom folders or a manual executable if a launcher scan misses a game. Select an item to save its CPU profile; **Save** persists a rule, while **Optimize/Apply** attempts to change currently running matching processes. You can also use **Quick Start** on the Dashboard to launch a game and apply Session Optimization in one action.

Use **Session Optimization** to choose what background processes may be suspended while gaming, preview the candidate list, and run an automatic or manual session. FrameHub records suspended processes and restores them safely upon session completion or recovery; it does not terminate them.

**Processes & CPU** provides direct control over running processes, allowing immediate assignment of CPU Sets, Processor Affinity, or process priority. **Profiles & Rules** manages persistent rules applied automatically by the profile watcher.

For CS2, close the game before scanning, editing, or restoring config. FrameHub creates backups and will not silently choose among ambiguous Steam userdata accounts. Hardware Monitor is session-only and lease-controlled. Settings includes tray, language, logs, startup, Companion pairing, and optional elevation. Data and logs are stored under `%APPDATA%\FrameHub`.

When CS2 config is detected in exactly one Steam userdata folder, FrameHub uses it automatically. If several valid folders exist, choose the numeric Steam userdata ID in the CS2 detail view. Until then, FrameHub blocks editable config reads and all CS2 write actions. New backups are kept separate for each resolved userdata path.

---

## LAN Companion & Game CPU Assignment

1. In Desktop FrameHub, navigate to **Settings > Companion**. Ensure Companion is enabled.
2. Open the pairing modal to display the LAN address and pairing QR code.
3. On your mobile device (connected to the same local Wi-Fi/LAN), scan the QR code or open the displayed URL in your mobile browser.
4. Complete the one-time pairing to establish a secure session token.
5. In the Companion web interface:
   - **Telemetry**: View real-time CPU, GPU, RAM, and live PresentMon telemetry streamed over WebSocket.
   - **Library & Quick Actions**: Browse detected games and launch them remotely.
   - **Game CPU Assignment**: When a game is active, temporarily adjust CPU core scheduling using topology presets (All Cores, Physical Only, Clear) or custom CPU Sets.
   - **Session Optimization**: Monitor and control background app suspension.
   - **Benchmarks**: Monitor active benchmark progress or trigger remote capture.

---

## Benchmarks

1. Add or scan the game in **Game Library**.
2. Start the game.
3. Open **Benchmarks**, or select the game and press **Benchmark** in its Library detail.
4. Select 30, 60, 120 seconds or a custom 10–600 second duration, plus an optional countdown.
5. Start the test, return focus to the game, and reproduce the intended scene/workload.
6. Review Average FPS, 1% Low, 0.1% Low, P95/P99 frame time, environment metadata (OS, CPU, GPU driver, RAM, display), quality warnings and the frame-time graph.
7. Repeat under the same game, graphics, scene and system conditions.
8. Use **Compare** for two completed sessions of that game.

The global benchmark hotkey is unassigned and disabled by default. Configure it under **Settings > Benchmark hotkey** by selecting **Change / record shortcut**, then pressing a supported modifier combination (or F8–F12). The same shortcut starts and stops capture while FrameHub is focused, minimized, in the tray, or while the game has focus. A hotkey start is immediate; the normal Start button still uses the configured countdown, and the saved countdown is not changed. FrameHub implements this with the Windows `RegisterHotKey` API and does not install a keyboard hook.

The hotkey uses the selected running library game. If none is selected, FrameHub proceeds only when exactly one benchmarkable library game is running. When multiple games make the target ambiguous, it logs the event and asks you to select the game in FrameHub instead of guessing.

Average FPS describes throughput; 1%/0.1% Low and high frame-time percentiles help expose intermittent stutter and pacing problems. They do not identify the cause by themselves. Capture records the current CPU profile, Session Optimization state, and system environment metadata, but never changes game processes directly. **History** includes local WPF and developer-harness schema-v1 sessions, can open their folder, and can permanently delete one validated session.

Data remains under `%LOCALAPPDATA%\FrameHub\Benchmarks`; settings and logs remain under `%APPDATA%\FrameHub`. No benchmark data is uploaded. If the benchmark engine is unavailable, repair/reinstall FrameHub—the single Setup contains the pinned PresentMon prerequisite. FrameHub itself does not inject into games or read game memory; compatibility with a particular game or anti-cheat may vary.
