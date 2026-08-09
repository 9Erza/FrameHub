# Changelog

## [Unreleased]

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
