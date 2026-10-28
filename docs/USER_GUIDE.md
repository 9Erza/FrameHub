# FrameHub User Guide

[Polski](USER_GUIDE.pl.md)

## Installing or upgrading v1.0.0

Download `FrameHub-Setup-1.0.0.exe` from the **Assets** section of the official [GitHub Releases](https://github.com/9Erza/FrameHub/releases) page once published. Do not use the automatically generated source ZIP as an installer. The installer requires Windows 10/11 x64 and administrator permission, and includes the pinned PresentMon 2.5.1 MSI needed for benchmarking.

For an existing FrameHub Inno Setup installation, **close FrameHub and run the new installer without uninstalling the old version**. The historical Inno Setup `AppId` and previous directory are reused. The user's configuration, profiles and library are kept separately under `%APPDATA%\FrameHub`; benchmark history is under `%LOCALAPPDATA%\FrameHub\Benchmarks`. A backup of personal settings is recommended before a major version upgrade.

See the [v1.0.0 release notes (EN/PL)](RELEASE_NOTES_1.0.0.md). The application only checks for updates and directs you to GitHub; it does not download or execute the installer automatically.

---

On first launch, scan **Games & Optimization** (Game Library). Add custom folders or a manual executable if a launcher scan misses a game. Select an item to save its CPU profile; **Save** persists a rule, while **Optimize/Apply** attempts to change currently running matching processes. You can also use **Quick Start** on the Dashboard to launch a game and apply Session Optimization in one action.

Use **Session Optimization** to choose what background processes may be suspended while gaming, preview the candidate list, and run an automatic or manual session. FrameHub records suspended processes and restores them safely upon session completion or recovery; it does not terminate them.

**Processes & CPU** provides direct control over running processes, allowing immediate assignment of CPU Sets, Processor Affinity, or process priority. **Profiles & Rules** manages persistent rules applied automatically by the profile watcher.

For CS2, close the game before scanning, editing, or restoring config. FrameHub creates backups and will not silently choose among ambiguous Steam userdata accounts. Hardware Monitor is session-only and lease-controlled. Settings includes tray, language, logs, startup, Companion pairing, and optional elevation. Data and logs are stored under `%APPDATA%\FrameHub`.

When CS2 config is detected in exactly one Steam userdata folder, FrameHub uses it automatically. If several valid folders exist, choose the numeric Steam userdata ID in the CS2 detail view. Until then, FrameHub blocks editable config reads and all CS2 write actions. New backups are kept separate for each resolved userdata path.

---

## Redesigned interface (October 2026, Ember Signal)

The Desktop shell keeps every module but rearranges it: a 42 px titlebar, a 224 px rail with
grouped navigation, a two-pill Monitor/Session status strip, and a footer with session, monitor
and PresentMon state plus `MIT · version`. The rail footer links the author (`9Erza`),
`DobryPC.pl`, project support and About/licenses.

- **Dashboard** ("Panel główny" in Polish): a hero with the primary action, 4 clickable metric
  cards (Monitor, Session, Benchmarks with an honest PresentMon state, Library), a Top-CPU
  process preview and the last 3 activity entries. Card actions navigate (`NavigateMetricCommand`);
  Quick Start stays unavailable while a benchmark is active or reserved.
- **Library**: one primary split-scan button (all/launcher sources), a sources segment, an
  X/Y ready bar, search/filter and a virtualized list; empty state offers Scan / Add folder /
  Add executable.
- **Session**: a 4-step wizard (configure → preview → start → restore) with sticky Restore.
- **Benchmarks**: game → engine-cause state machine (it tells you *which* cause blocks capture:
  no game, missing PresentMon service, no permission, hotkey unset) → configure → start;
  History/Compare with CSV/Markdown export through a save dialog that reports write errors.
- **Processes**: 7 columns plus a rule column (`none` / profile name), sort/filter, per-row
  detail with CPU Sets/Affinity/Priority and topology presets; virtualization kept; Riot and
  benchmark-active rows are skipped with a stated reason. Mutations revalidate identity first.
- **Profiles**: creator-first flow, 0–50 rows with search, per-row enable toggle, conflict
  warnings, validation and explicit Auto Apply consent.
- **Hardware**: OFF / ON (lease) / UNAVAILABLE states, optional storage sensors
  (`EnableStorageSensors`) and a read-only timer-resolution card.
- **Logs**: severity filter + search, a visible 200-of-N cap and a Clear action that asks
  for confirmation.
- **Settings**: 6 categories (General, Language, Startup & Window, Companion & Network,
  Benchmarks, Advanced & Diagnostics). **Companion & Network** hosts the read-only network
  diagnostics panel: adapters, current resolvers (automatic/DHCP or static), popular providers
  (Cloudflare, Google Public DNS, Quad9, OpenDNS) with IPv4/IPv6/DoH latency comparison.
  The panel states explicitly that nothing is applied — there is no mutation code by design.
  Restart offers normal cooperative restart (successor-first, settings preserved) and
  restart-as-administrator, both with success/timeout/refusal feedback.

Language: **Settings > Language** (or the tray/Settings switch) persists `Language: pl|en`
to `settings.json` (default `en`). The full Desktop dictionary holds 1005 keys per language
with no gaps; Companion has its own parity-tested dictionary. Export files always use
invariant formatting and UTC timestamps regardless of UI language.

---

## LAN Companion & Game CPU Assignment

1. In Desktop FrameHub, navigate to **Settings > Companion & Network**. Ensure Companion is enabled.
2. Open the pairing modal to display the LAN address and pairing QR code.
3. On your mobile device (connected to the same local Wi-Fi/LAN), scan the QR code or open the displayed URL in your mobile browser.
4. Complete the one-time pairing to establish a secure session token.
5. In the Companion web interface:
   - **Telemetry**: View real-time CPU, GPU, RAM, and live PresentMon telemetry streamed over WebSocket.
   - **Library & Quick Actions**: Browse detected games and launch them remotely.
   - **Game CPU Assignment**: When a game is active, temporarily adjust CPU core scheduling using topology presets (All Cores, Physical Only, Clear) or custom CPU Sets.
   - **Session Optimization**: Monitor and control background app suspension.
   - **Benchmarks**: Monitor active benchmark progress or trigger remote capture.
6. The Companion adapts to the screen: on wide desktop browser windows it shows a side navigation rail with multi-column content grids; on phones it behaves as a mobile app shell — the bottom navigation stays fixed while content scrolls, it works in mobile Safari/Chrome, and it can be added to your home screen with the FrameHub icon. The running product version is shown in **Settings > About** (and in the footer).

---

## Update Checks

With **Settings > Check for updates at startup** enabled, FrameHub checks for a new release once per application run — but only after the main window is actually shown to you. It never interrupts silent tray/minimized startup, stays silent when you are up to date or offline, and shows a FrameHub-styled dialog with a direct link to the release page when a new version is available. **Settings > Check now** performs a manual check at any time and uses the same dialog; no installers are downloaded or executed automatically.

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

Data remains under `%LOCALAPPDATA%\FrameHub\Benchmarks`; settings and logs remain under `%APPDATA%\FrameHub`. No benchmark data is uploaded. If the benchmark engine is unavailable, the Benchmarks view names the specific cause (no game selected/running, missing PresentMon service, no permission, or hotkey unset) instead of a generic reinstall hint. The single Setup contains the pinned PresentMon prerequisite — repair or reinstall it only when the engine panel reports the service itself missing. FrameHub itself does not inject into games or read game memory; compatibility with a particular game or anti-cheat may vary.
