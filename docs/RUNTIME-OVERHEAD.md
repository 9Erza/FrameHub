# FrameHub Runtime Resource & Overhead Audit

This document records the methodology, baseline measurements, component lifetime analysis, and findings for the pre-release runtime resource audit of FrameHub.

---

## 1. Executive Summary

- **Context on Previously Observed ~400 MB Task Manager Observation**: In controlled standalone Release execution, `FrameHub.App` runs with an active-window Working Set of **~310–330 MB** after warm-up and falls substantially to **~90–105 MB** when minimized to the system tray. The previously observed ~400 MB value was not reproduced under standalone Release testing and may depend on build configuration, attached developer tooling, or runtime state.
- **Process Memory Profile**: Target-process managed GC heap was not directly measured because cross-process runtime diagnostics (`dotnet-counters`, `EventPipe`, `CLRMD`) were unavailable in the audit environment. Conceptual breakdown of the resident working set includes managed objects, CLR/JIT runtime execution pages, WPF hardware-accelerated Direct3D rendering surfaces, DirectWrite font caches, native library mappings, thread stacks, and OS handle tables.
- **Idle Soak Stability**: A 10-minute continuous idle measurement in Release mode demonstrated a stable plateau reached at roughly 3.5–5 minutes, with approximately +0.94 MB change between 3.5 minutes and 10 minutes, and an average idle CPU usage of **~0.23%–0.25%** with the active window (0.00% when minimized to tray).
- **Finding Classification**: **No evidence of a release-blocking idle runtime leak was observed during the measured 10-minute idle soak.** All observed resource costs are stable, bounded, and justified by feature requirements.

---

## 2. Authoritative Process-Level Runtime Measurements

Measurements were performed on .NET 10 Release builds without elevation or attached diagnostic tools, sampling process metrics directly from `FrameHub.App.exe` (`WorkingSet64`, `WorkingSetPrivate`, `PrivateMemorySize64`, `VirtualMemorySize64`, `HandleCount`, `Threads.Count`, and normalized CPU %):

| Measurement State | Scenario Description | Working Set | Private WS | Private Bytes | Handles | Threads | Normalized CPU % |
|---|---|---:|---:|---:|---:|---:|---:|
| **State A (Cold Startup)** | Initial render at 15 seconds | ~294 MB | ~165 MB | ~245 MB | ~2500 | ~37 | ~1.5% |
| **State A (Warm-up)** | Active window at 60 seconds | ~310 MB | ~179 MB | ~258 MB | ~2465 | ~27 | ~0.3% |
| **State A (Plateau Phase)** | Active window at 3.5 minutes | ~328 MB | ~195 MB | ~274 MB | ~2460 | ~25 | ~0.25% |
| **State A (10-Min Soak)** | Active window at 10 minutes | ~329 MB | ~196 MB | ~275 MB | ~2472 | ~27 | ~0.23% |
| **State A (Tray Minimized)** | Minimized to tray baseline | ~99 MB | ~43 MB | ~77 MB | ~716 | ~15 | ~0.0% |

*Note: Memory continued warming during the initial few minutes as WPF visual elements and Kestrel subsystems initialized, reaching a stable plateau at roughly 3.5–5 minutes (with only +0.94 MB growth over the final 6.5 minutes).*

---

## 3. Working Set Composition (Conceptual)

In a hardware-accelerated .NET 10 WPF desktop application, the total resident working set is composed of several architectural layers:

1. **WPF & Direct3D Graphics Pipeline**: Hardware-accelerated DirectX/D3D swapchains, DWM surfaces, DirectWrite font caches, and compositor buffers.
2. **CLR Runtime & JIT Metadata**: JIT-compiled native code pages, assembly metadata tables, and execution engine structures.
3. **Managed Application Objects**: ViewModels, domain models, navigation state, and in-memory caches.
4. **Native Library Mappings & OS Structures**: Loaded dynamic link libraries, thread stack allocations, and OS kernel handle structures.

---

## 4. Lifecycle Verification & Synthetic Diagnostics

Beyond the standalone process soak, feature-specific lifecycle mechanisms were evaluated:

### 4.1 Hardware Monitoring Lifecycle
- **Architectural Gate**: Hardware sensors are completely closed (`_computer.Close()`) when `HardwareMonitorEnabled == false` or when the active consumer count returns to 0.
- **Verification**: Verified via architectural analysis and automated lifecycle tests (`HardwareMonitorRuntimeStateTests.cs`).
- **Synthetic Estimate**: In-process diagnostic runs indicate that an active LibreHardwareMonitor consumer adds ~20–30 MB of native driver handle/sensor structures and ~0.3%–0.5% CPU during 1 s updates, which are fully released upon lease disposal.

### 4.2 Companion Server & Streaming Lifecycle
- **Server Listener**: Kestrel socket listener runs on localhost/LAN when enabled (synthetic memory footprint ~15 MB, 0% CPU when idle).
- **WebSocket Streaming**: Active telemetry streams at 500 ms intervals per connected client. Disconnection immediately cancels streams and releases the associated hardware lease.
- **Verification**: Verified via architectural analysis and integration test suites (`SettingsCompanionIntegrationTests.cs`, `LanSecurityIntegrationTests.cs`).

### 4.3 PresentMon Lifecycle & Preemption
- **Dormant State**: When no game is active, `LivePerformanceTelemetryService` creates NO PresentMon sessions and remains dormant with 250 ms delay checks.
- **Arbitration & Preemption**: Live PresentMon is preempted immediately during benchmark capture via `ILivePresentMonPreemption`.
- **Verification**: Verified via architectural review and automated arbitration tests (`LivePerformanceTelemetryTests.cs`).

---

## 5. Background Work & Timer Inventory

Recurring background activities adhere to single-owner architecture and lease controls:

1. **Profile Watcher** (`AppRuntimeService`): Configurable interval (default 2 s); performs targeted name queries for enabled profiles only.
2. **Processes Page Refresh** (`ProcessesViewModel`): Runs only while on the Processes page (`Start()` on navigation, `Stop()` on leaving); sole CPU delta sampler.
3. **Session Auto Detection** (`SessionOptimizationViewModel`): 3 s timer, active only when Auto Mode is explicitly enabled by user.
4. **Hardware Refresh** (`HardwareViewModel`): Lease-backed; runs only while on the Hardware page and `HardwareMonitorEnabled == true`.
5. **Benchmark Target Detection** (`BenchmarkViewModel`): 5 s timer, active only while on the Benchmarks page; stops on leaving.
6. **CS2 Process Check** (`LibraryViewModel`): 2 s timer, active only while CS2 item is selected.
7. **Active Game Monitor** (`ActiveGameMonitor`): 2 s evaluation loop, active while Companion is enabled.
8. **Companion Publisher** (`AppTelemetrySnapshotProvider`): 500 ms periodic timer loop; samples hardware only when `HardwareMonitorEnabled == true` and consumer count > 0.
9. **Live PresentMon** (`LivePerformanceTelemetryService`): 250 ms loop; dormant/sessionless when no eligible game is running; preempted during benchmarks.
10. **WebSocket Sender** (`TelemetryWebSocketHandler`): 500 ms stream per connected WebSocket client; holds 1 hardware lease.

---

## 6. Audit Conclusions

1. **Release Readiness**: No evidence of a release-blocking idle runtime leak was observed during the 10-minute soak test. While this measurement does not mathematically prove the absence of extremely long-duration leaks or game-specific anomalies, no idle runaway exists.
2. **Resource Stability**: Memory stabilizes around 328–329 MB Working Set with an active window and falls to approximately 99 MB when minimized to the tray. Idle CPU remains low (~0.23%–0.25%).
3. **No Speculative Optimization**: No product-code optimization or volatile memory-trimming hacks (such as periodic `GC.Collect()` or `EmptyWorkingSet`) are warranted or permitted.
4. **Manual Verification Smoke**: Active gameplay with live PresentMon sessions and multi-device LAN connectivity remain recommended manual smoke verification items for release sign-off.
