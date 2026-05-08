# FrameHub.GameData

This folder contains declarative game optimization data used by FrameHub.

The application engine reads these JSON files and builds the UI/options dynamically.
Do not put executable scripts, PowerShell, batch files, DLLs or binary patches here.

Current games:

- `counter-strike-2`

Planned online model:

1. Built-in data ships with the app from this folder.
2. Updated data can later be downloaded to `%APPDATA%/FrameHub/GameData`.
3. User/local AppData data has priority over built-in data.
