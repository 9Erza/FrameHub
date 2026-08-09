# Benchmarking architecture (FrameHub v0.6 development)

This document is the implementation contract for the v0.6 benchmarking subsystem. No benchmark UI is included yet.

## Production capture architecture

FrameHub benchmarking uses the installed Intel PresentMon Shared Service/API:

```text
FrameHub
  -> matching PresentMonAPI2.dll installed with PresentMonSharedService
  -> PresentMonSharedService
  -> exact-PID frame query and pmConsumeFrames
  -> BenchmarkFrameSample
  -> FrameHub BenchmarkAnalyzer and storage/history
```

The proven environment uses PresentMon v2.5.1, API 3.3.0, service `PresentMonSharedService`, and the matching middleware at `%ProgramFiles%\Intel\PresentMonSharedService\PresentMonAPI2.dll`. Runtime discovery first honors the developer-only `--presentmon-api-dll` absolute-path override, then resolves the DLL beside the configured Windows service executable, then checks the official installation directory. FrameHub does not copy or load an arbitrary private middleware DLL.

The standalone PresentMon console executable and CSV capture path are retired. Production capture does not launch `PresentMon.exe`, construct console arguments, create a named `FrameHub_*` ETW session, parse console CSV, capture child stdout/stderr, or fall back to a console backend. Intel's PresentMon Capture Application can be useful to developers as a diagnostic/control client, but FrameHub does not use it for normal capture.

FrameHub does not inject DLLs into games, read or modify game memory, hook graphics APIs, request debug privileges, install a kernel driver, or implement anti-cheat bypasses. PresentMon owns ETW collection behind its documented Shared Service/API boundary.

## Verified capture contract

FrameHub opens an API session, reads introspection, registers the frame query, starts exact-PID tracking, consumes frames for the requested duration, flushes/finally drains buffered frames, stops tracking, frees the frame query, closes the session, and unloads its DLL handle. Cleanup is attempted independently in that order after normal completion, cancellation, API failure, or partial initialization.

The production query is intentionally fixed to the real-machine-validated set:

- `SwapChainAddress` (`UInt64`)
- `PresentRuntime` (`Enum`)
- `PresentMode` (`Enum`)
- `CpuStartQpc` (`UInt64`)
- `BetweenPresents` (`Double`)
- `DisplayedTime` (`Double`)
- `BetweenDisplayChange` (`Double`)
- `FrameType` (`Enum`)

Every metric is registered only when introspection confirms a usable frame-event type. Query-returned offsets and sizes are bounds-checked against the returned blob size, and each populated frame uses its own `buffer + index * blobSize` base. `pNumFramesToRead` is reset to full capacity before every consume call.

## Legacy ETW-session postmortem

The retired console backend passed `--session_name FrameHub_<benchmark-session-guid>` to the standalone child process. The child therefore created and owned the named ETW session. Normal timed/process-exit completion relied on PresentMon shutting that session down while exiting.

The cancellation path instead called `Process.Kill(entireProcessTree: true)` and then waited for the terminated child. There was no owner-scoped post-kill ETW stop operation, so an abruptly terminated console process could leave its named kernel ETW session running. This was the code path that permitted the observed stale `FrameHub_*` session. The backend and its session-creation code have been removed; FrameHub does not perform unsafe prefix-based cleanup of system ETW sessions.

The observed recovery involved both stopping the stale session and restarting `PresentMonSharedService`, so it does not prove which individual action restored capture. It does prove that the old backend had an ETW ownership leak and that the Shared Service/API data path works in a clean state.

## Identity and safety

A benchmark retains its library identity and an exact Windows process identity: PID, normalized name, UTC process start time, and normalized executable path when ordinary managed process access permits it. PID, name, and start time protect against PID reuse. Identity is validated before and after capture.

Executable-path resolution uses only `Process.MainModule.FileName`. If access is denied or the path is unavailable, FrameHub respects that restriction and continues with a pathless identity. It does not call native process-open/path-recovery APIs, WMI/CIM, enumerate additional modules, read process memory, or request debug privileges. A pathless library-name association is allowed only for one unambiguous configured executable-name match.

## Storage and lifecycle

Machine-local captures are stored below `%LOCALAPPDATA%\FrameHub\Benchmarks`:

```text
<source>-<safe-stable-id>-<identity-hash>\
  <UTC timestamp>_<session GUID>\
    session.json
    summary.json
    raw-frames.json
```

`session.json` records identity and capture state. `raw-frames.json` preserves API-produced `BenchmarkFrameSample` values. `summary.json` contains analyzer results, swap-chain selection, and quality diagnostics. Status progresses from `Created` to `Capturing` and then `Completed`; cancellation becomes `Cancelled`, and other failures become `Failed`. A summary is written only after successful analysis.

## Analysis contract

The primary presented stream uses finite positive `MsBetweenPresents` values for the exact target PID and selected swap chain. FrameHub retains its own deterministic AVG FPS, median, 1% low, 0.1% low, P95/P99, minimum, and maximum calculations. No cosmetic outlier removal is applied.

`DisplayedTime` is the current frame's displayed duration. `MsBetweenDisplayChange` describes the previous displayed frame's interval and remains a separate metric stream; neither is substituted for the other. Unavailable data remains null rather than becoming zero.

Swap chains are ranked by usable presented-frame count, active duration, continuity, then address. Frame types and dropped/not-displayed state are preserved when available. Quality remains categorical (`Valid`, `Warning`, `Invalid`) and uses the versioned FrameHub quality policy.

## Developer harness

The harness is developer-only and is not referenced by the WPF application or installer:

```powershell
dotnet run --project .\FrameHub.BenchmarkHarness --configuration Release -- `
  --backend api `
  --pid 12345 `
  --seconds 30
```

`--backend api` is accepted for explicitness; any retired `csv` value is rejected. Other options are `--game-id`, `--presentmon-api-dll`, and `--output`. The harness prints API version, registered metric types, blob/buffer dimensions, consume-call counts, sample decode counts, analyzer results, quality, and storage location.

## Packaging contract

The final user experience will be one FrameHub installer that handles the official PresentMon prerequisite. End users must not be required to download PresentMon separately, locate its middleware DLL, or configure the service manually. Installer chaining, prerequisite ownership, coexistence, upgrade, and uninstall behavior are deferred to a later task; the current installer and stable FrameHub AppId are unchanged.

## Known limitations

- There is no Benchmark page or end-user capture workflow yet.
- OS, GPU/driver support, overlays, variable refresh, frame generation, and application behavior can affect PresentMon data.
- Analysis version 1 does not segment scenes, warm-up, pauses, loading screens, or frame types.
- Raw QPC timestamps are retained but are not used for continuity without frequency metadata.
