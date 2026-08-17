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

## Planned

- Additional game integrations and clearer before/after reporting.

M10.1 deliberately uses one Stop action: FrameHub first requests a normal GUI close, waits briefly, then may force-terminate only the exact revalidated process instance belonging to the opted-in Library item. Apps that launch under a different executable identity are not controlled in this milestone.

M10.2 deliberately keeps existing Manual session semantics: Gaming Mode launches the selected Game-type Library item (or skips launch when it is already running) and delegates session start to `SessionOptimizationCoordinator` with the existing `Manual` trigger. Game-exit auto-restore, Dashboard favorites/recents, and live Desktop telemetry are out of scope.

## Riot Games support boundaries

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
