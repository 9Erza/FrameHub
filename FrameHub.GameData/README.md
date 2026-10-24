# FrameHub.GameData

> **Status: dormant (2026-10-24).** This data set is not referenced by production code
> (no `.csproj`, absent from `FrameHub.slnx`, zero consumers of `GameDataService` /
> `GameDataModels`). Canonical state: `docs/architecture/OVERVIEW.md` and
> `docs/architecture/FEATURE-MAP.md`. Adopt-or-remove is a pending P3 decision —
> see `docs/audits/2026-10-24-noc-functional-inventory.md`. Do not treat the text
> below as the live integration contract.

This folder contains declarative game optimization data used by FrameHub.

The application engine reads these JSON files and builds the UI/options dynamically.
Do not put executable scripts, PowerShell, batch files, DLLs or binary patches here.

Current games:

- `counter-strike-2`

Planned online model:

1. Built-in data ships with the app from this folder.
2. Updated data can later be downloaded to `%APPDATA%/FrameHub/GameData`.
3. User/local AppData data has priority over built-in data.
