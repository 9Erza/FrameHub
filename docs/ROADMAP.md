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
- Companion Game CPU Control V1 & UX Polish: active-game temporary CPU scheduling override (Affinity / CPU Sets), separate Session Optimization (background apps) and Game CPU Control cards, CPU Sets recommendation, topology-driven presets (All, Physical only, Clear), and dedicated scopes (`read:optimization-cpu`, `write:optimization-cpu`).

## Planned Near-Term Backlog

1. **Companion Game Library UX**:
   - Display game icons in library item list.
   - Investigate, remove, or fix unexplained "Desktop Game" placeholder entries.
   - Gracefully handle launch errors if a library item references a missing or moved executable.
2. **Companion Navigation & Branding**:
   - Replace generic lightning icon with official FrameHub application logo where appropriate.
   - Ensure clicking the header brand/logo navigates directly to the Home tab.
3. **Session Optimization & Game CPU Control Follow-ups**:
   - Maintain clear separation between Session Optimization (background process suspension/recovery) and Game CPU Control (active game affinity / CPU Sets scheduling).
   - Continue promoting CPU Sets as recommended where supported by platform topology.
4. **Desktop Game Library UX**:
   - Refine user-facing naming (e.g. evaluating "Gry i optymalizacja" / equivalent) to communicate game-specific optimization capabilities.
   - Preserve clear product distinction from generic "Procesy i CPU" tools.
5. **Desktop Dashboard Presentation**:
   - Eliminate raw developer/class-name strings (e.g., `FrameHub.Core.Models.Library.LibraryItem`) from user-facing views.
   - Clarify and redesign the "Tryb gaming" / "Gaming Mode" quick action card so users immediately understand the exact actions executed.
6. **Companion Settings Refinement**:
   - Simplify "Język prezentacji" label to clean, user-friendly "Język" / "Language".
   - Evaluate a concise, high-value set of Companion preferences rather than filler settings.
7. **Runtime Resource & Overhead Audit**:
   - Separately measure FrameHub.App CPU and RAM footprint during idle, gaming, and benchmark capture workloads.
   - Identify and eliminate evidence-backed background polling or observation waste.
   - Distinguish FrameHub process overhead from development/IDE background processes.

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
