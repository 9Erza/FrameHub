# Changelog

## [Unreleased]

## [0.7.0] - 2026-08-18

- Added the LAN Companion Server (ASP.NET Core / Kestrel) and mobile web UI with cryptographically secure pairing, QR code connection, opaque session tokens, and granular permission scopes (`read:library`, `write:library-launch`, `read:telemetry`, `read:benchmarks`, `write:benchmarks`, `read:session-optimization`, `write:session-optimization`, `read:optimization-cpu`, `write:optimization-cpu`, `read:background-apps`, `write:background-apps`).
- Added real-time hardware telemetry streaming via WebSocket with automatic reconnection and fallback, accompanied by lease-controlled background hardware monitoring and automated sensor shutdown.
- Added Game CPU Assignment (`Przydział CPU dla gry` / `Game CPU Assignment`): active-game temporary CPU scheduling override (Processor Affinity and CPU Sets) with topology presets (All Cores, Physical Only, Clear) and hardware-aware CPU Sets recommendations.
- Added Gaming Quick Actions / Gaming Mode on Dashboard: one-click unified launch combining trusted Library game startup, shared launch cooldown arbitration, and Session Optimization lifecycle management.
- Added Benchmark Environment Metadata v1: one-shot, best-effort environment context capture (OS build, CPU model, GPU name and driver version, system RAM, primary display resolution and refresh rate, FrameHub version) stored alongside benchmark captures, surfaced in Desktop benchmark details, and compared across sessions.
- Added trusted Background App Control in Companion for explicitly opted-in library background tools with process identity revalidation and benchmark preemption arbitration.
- Added conservative Riot Games management (League of Legends, VALORANT): passive Start Menu shortcut discovery, official shortcut launching, and strict non-mutation / non-benchmark exclusion for Riot game, client, and Vanguard processes.
- Added Missing Executable handling across Desktop and Companion with safe disabled action states and user-facing status indicators.
- Added client-side game icon caching via IndexedDB in Companion web interface with official branding and refined UI polish.
- Completed comprehensive Runtime Resource & Overhead Audit: confirmed bounded idle resource consumption, flat long-duration idle stability, and lease-controlled hardware monitor shutdown.
- Redesigned and polished desktop navigation ("Games & Optimization" / "Gry i optymalizacja"), Quick Start cards, ComboBox display styling, and dual-language Polish/English localization.

## [0.6.0] - 2026-08-09

- Added production benchmarking through the PresentMon Shared Service/API with exact-PID capture, automatic running-game detection from Game Library, asynchronous cancellation, readiness reporting and diagnostics.
- Added the WPF Capture, History and same-game Compare workflow with local session management, benchmark context, quality warnings and a spike-preserving frame-time graph.
- Added Average FPS, median FPS, 1% Low, 0.1% Low, frame-time percentile reporting and comparison deltas backed by FrameHub's existing analyzer semantics.
- Added the configurable Windows global benchmark Start/Stop hotkey without keyboard hooks.
- Added schema-v1 history discovery compatible with developer-harness sessions, corrupt-session isolation, safe deletion, Dashboard and Game Library integration, and English/Polish localization.
- Bundled the pinned official PresentMon v2.5.1 MSI inside the single FrameHub installer, with hash verification, prerequisite repair/reuse checks, licensing and third-party notices.
- Redesigned the application shell and feature pages with a denser dark-slate visual system, consistent shared controls and simplified Segoe UI typography.

## [0.5.0] - 2026-08-09

- Completed the major FrameHub UI/UX overhaul.
- Improved Session Optimization reliability and recovery.
- Hardened Windows startup configuration and verification.
- Added path-aware process profile matching.
- Improved Hardware Monitor lifecycle and background polling.
- Added deterministic CS2 Steam userdata selection and account-scoped backups.
- Added collision-safe CS2 backup naming.
- Improved game-library filtering.
- Added Polish and English documentation.
- Added CI and expanded regression coverage.
- Added Inno Setup packaging with direct upgrade support from FrameHub 0.4.x.
