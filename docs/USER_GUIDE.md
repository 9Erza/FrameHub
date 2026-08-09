# FrameHub User Guide

[Polski](USER_GUIDE.pl.md)

On first launch, scan **Game Library**. Add custom folders or a manual executable if a launcher scan misses a game. Select an item to save its CPU profile; **Save** persists a rule, while **Optimize/Apply** attempts to change currently running matching processes.

Use **Session Optimization** to choose what may be suspended, preview the result, then run an automatic or manual session. FrameHub records suspended processes and resumes them during recovery; it does not kill them.

**Processes & CPU** is for immediate running-process control. **Profiles & Rules** manages saved rules and the watcher. Profiles with executable paths only target that normalized path; legacy profiles without one use name matching.

For CS2, close the game before scanning, editing, or restoring config. FrameHub creates backups and will not silently choose among ambiguous Steam userdata accounts. Hardware Monitor is session-only and must be enabled explicitly. Settings includes tray, language, logs, startup, and optional elevation. Data and logs are under `%APPDATA%\FrameHub`.

Troubleshooting: refresh the library/process list, verify the executable path, close CS2 before config edits, and use Logs for failure details. Protected processes can require administrator permission.

When CS2 config is detected in exactly one Steam userdata folder, FrameHub uses it automatically. If several valid folders exist, choose the numeric Steam userdata ID in the CS2 detail view. Until then, FrameHub blocks editable config reads and all CS2 write actions. New backups are kept separate for each resolved userdata path.
