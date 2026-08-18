# Validation & Testing Standards

This document defines the authoritative validation commands and testing safety principles for FrameHub.

---

## Authoritative Validation Commands

Every feature implementation and review must execute the following validation commands to ensure zero warnings, zero errors, test suite parity, clean JavaScript syntax, and diff hygiene.

### 1. Solution Release Build
```powershell
dotnet build --configuration Release
```
*Requirement*: 0 errors, 0 warnings.

### 2. Desktop & Core Test Suite
```powershell
dotnet test FrameHub.Tests/FrameHub.Tests.csproj --configuration Release --no-build
```
*Requirement*: 100% pass rate (no skipped/failed tests).

### 3. Companion Transport & API Test Suite
```powershell
dotnet test FrameHub.Companion.Tests/FrameHub.Companion.Tests.csproj --configuration Release --no-build
```
*Requirement*: 100% pass rate.

### 4. JavaScript Syntax Validation
Run this PowerShell pipeline to validate all frontend modules:
```powershell
Get-ChildItem -Path "FrameHub.Companion/wwwroot/js/*.js" | ForEach-Object { node --check $_.FullName }
```
Or execute against each explicit file:
```powershell
node --check FrameHub.Companion/wwwroot/js/app.js FrameHub.Companion/wwwroot/js/auth-transport.js FrameHub.Companion/wwwroot/js/benchmarks.js FrameHub.Companion/wwwroot/js/i18n.js FrameHub.Companion/wwwroot/js/library.js FrameHub.Companion/wwwroot/js/session-optimization.js FrameHub.Companion/wwwroot/js/telemetry.js
```
*Requirement*: Clean exit with 0 errors across all frontend modules. Do not rely on shell glob expansion (`*.js`) directly with Node, as Node does not expand wildcards on Windows.

### 5. Diff & Formatting Hygiene
```powershell
git diff --check
```
*Requirement*: Zero whitespace errors, zero trailing spaces, zero conflict markers.

### 6. Scope & Working Tree Verification
```powershell
git status --short --untracked-files=all
git diff --stat
```
*Requirement*: Only expected files are modified; no untracked build artifacts or accidental edits.

---

## Testing Principles & Safety

Tests must match risk and adhere strictly to non-destructive principles:

### Synthetic Seams for Native Behavior
- **Never Casually Mutate the Host OS**: Unit and integration tests must **NEVER** alter real process affinities, mutate real CPU Sets, suspend user applications, terminate running processes, launch real games, query real hardware sensors, or call kernel drivers (PawnIO).
- **Use Dedicated Synthetic Seams**: Use interface abstractions and delegates specifically designed for testability (e.g. `ISessionCpuControlBackend`, `IHardwareMonitorBackend`, `IPresentMonBackend`).
- **Do Not Bloat Production Code for Testing**: Do not introduce heavy dependency-injection frameworks or unnecessary public abstractions solely to test trivial private logic if an existing clean seam can be used.

### Isolation and Cleanliness
- **File System Isolation**: Tests modifying persisted data (profiles, paired devices, session WAL) must use isolated temporary directories and clean them up deterministically in `TestCleanup` or `finally` blocks.
- **Network Port Independence**: Companion integration tests must dynamically allocate free loopback ports (`GetFreePort()`) to prevent test runner collisions.
- **Fail-Closed Verification**: Security and safety tests must explicitly verify fail-closed behavior (e.g., stale session tokens, mismatched PIDs, missing scopes, and anti-cheat protected game identities).
