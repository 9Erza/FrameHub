# FrameHub Agent Instructions

FrameHub is a Windows gaming performance toolkit built with .NET 10 (WPF desktop app), Intel PresentMon Shared Service/API, and an ASP.NET Core LAN Companion server with a vanilla JavaScript mobile web frontend.

---

## 1. Mandatory Read Order

Before planning or editing code, agents must follow this reading order:
1. **[AGENTS.md](AGENTS.md)** (this operating contract).
2. **[docs/agents/AREA-GUIDE.md](docs/agents/AREA-GUIDE.md)** (locate the authoritative owner for your task).
3. **Canonical Architecture Docs**:
   - [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — Architecture landing page.
   - [docs/architecture/OVERVIEW.md](docs/architecture/OVERVIEW.md) — Composition, layers, persistence.
   - [docs/architecture/SERVICE-CATALOG.md](docs/architecture/SERVICE-CATALOG.md) — Service catalog & ownership.
   - [docs/architecture/INVARIANTS.md](docs/architecture/INVARIANTS.md) — Hard safety & authority invariants.
   - [docs/architecture/BACKGROUND-WORK.md](docs/architecture/BACKGROUND-WORK.md) — Inventory of timers & recurring loops.
   - [docs/BENCHMARKING.md](docs/BENCHMARKING.md) — Benchmark capture & PresentMon rules (if touching benchmarks).
   - [docs/ROADMAP.md](docs/ROADMAP.md) — Canonical roadmap (if changing product scope).
4. **Target Source & Tests**: Inspect existing implementation before writing code.

---

## 2. Non-Negotiable Rule: REUSE-FIRST / SINGLE-OWNER

> [!IMPORTANT]
> **Before creating ANY new Service, Backend, Manager, Coordinator, Provider with domain logic, persistent worker, timer, polling loop, cache, state machine, process scanner, hardware monitor, PresentMon owner, or native mutation implementation: FIRST search for an existing owner.**

- If an existing component already owns the responsibility: **DO NOT create a parallel owner or second implementation.** Extend, improve, or reuse the existing owner.
- **A new consumer is NOT a new responsibility**: Desktop UI, Companion UI, and REST API controllers are presentation layers. They do not justify creating new domain implementations.
- **Example**: Desktop needs CPU affinity, Companion needs CPU affinity, or a future API needs CPU affinity $\rightarrow$ **all of them must reuse `ProcessService`**. A new consumer is NEVER justification for creating `ProcessService2`, `CompanionCpuService`, or parallel native scheduling mechanics.

### New Abstraction Gate
Before introducing any new Service, Manager, Coordinator, Backend, or domain-bearing Provider, answer these 7 questions:
1. *What exact responsibility is new?*
2. *Which existing components were searched?*
3. *Why can the responsibility not coherently belong to an existing owner?*
4. *Does the new abstraction own state/lifecycle or merely adapt an existing owner?*
5. *Could this create two implementations of the same behavior?*
6. *Is this abstraction being created only because there is a new consumer/UI/API?* (If YES: that alone is NOT sufficient justification).
7. *Does this abstraction duplicate an existing owner's method semantics under a different name?* (If YES: reuse/extend the existing owner instead).

If concrete answers cannot be given: **DO NOT CREATE THE ABSTRACTION.**

### Adapter / Provider & Future Consolidation Rules
- **Delegation Only**: Narrow boundary adapters (e.g. between `FrameHub.Companion` and `FrameHub.App`) must **delegate only**. They must never duplicate process mutation, persistence, scheduling, validation policies, hardware monitoring, lifecycle, or benchmark mathematics.
- **No Precedent for Wrappers**: If an existing adapter/backend contributes no meaningful boundary responsibility and merely forwards calls, do NOT automatically create more adapters patterned after it. Prefer direct consolidation into the authoritative owner where project layering permits; do not perform opportunistic architectural cleanup during unrelated feature work.

---

## 3. Ownership Map: Authoritative Owners vs Boundary Adapters

### Authoritative Domain Owners
| Domain / Responsibility | Sole Authoritative Component | Key Reference |
|---|---|---|
| **Native CPU Affinity & CPU Sets Mechanics** | `ProcessService` *(sole native scheduling owner)* | [SERVICE-CATALOG.md](docs/architecture/SERVICE-CATALOG.md) |
| **Session Lifecycle, Recovery & Active Game CPU Policy** | `SessionOptimizationCoordinator` | [INVARIANTS.md](docs/architecture/INVARIANTS.md) |
| **Benchmark Lifecycle & Mutation Arbitration** | `BenchmarkCaptureCoordinator` | [INVARIANTS.md](docs/architecture/INVARIANTS.md) |
| **Live PresentMon Telemetry** | `LivePerformanceTelemetryService` | [SERVICE-CATALOG.md](docs/architecture/SERVICE-CATALOG.md) |
| **Process Suspend / Resume & Live Revalidation** | `ProcessSuspendService` | [SERVICE-CATALOG.md](docs/architecture/SERVICE-CATALOG.md) |
| **On-Demand Process Observation** | `ProcessObservationSnapshotProvider` (TTL 250ms, no timer) | [OVERVIEW.md](docs/architecture/OVERVIEW.md) |
| **Process Projections & CPU Delta Sampler** | `ProcessScannerService` (sole CPU delta sampler) | [OVERVIEW.md](docs/architecture/OVERVIEW.md) |
| **Runtime Composition & Profile Watcher** | `AppRuntimeService` | [OVERVIEW.md](docs/architecture/OVERVIEW.md) |
| **Hardware Sensor Monitoring** | `HardwareMonitorService` (lease-controlled, requires setting) | [BACKGROUND-WORK.md](docs/architecture/BACKGROUND-WORK.md) |
| **Desktop Library Launch** | `AppLibraryLaunchService` | [FEATURE-MAP.md](docs/architecture/FEATURE-MAP.md) |
| **Companion Pairing & Device Store** | `DeviceRecordStore` | [INVARIANTS.md](docs/architecture/INVARIANTS.md) |

### Boundary Adapters & Synthetic Test Seams (Delegating Only — No Domain Logic)
| Boundary Adapter / Seam | Target Authoritative Owner | Role & Constraints |
|---|---|---|
| `SessionCpuControlBackend` / `ISessionCpuControlBackend` | `ProcessService` | Thin native boundary / synthetic test seam; delegates CPU read/apply operations to `ProcessService`. NOT an independent scheduling owner. |
| `AppSessionOptimizationProvider` | `SessionOptimizationCoordinator` | Companion/App boundary adapter; delegates Session control to coordinator. |
| `AppLibraryProvider`, `AppBackgroundAppProvider` | `LibraryService`, `AppLibraryControlService` | Companion boundary adapters; shared cooldown via `LibraryLaunchReservationService`. |

---

## 4. Safety & Security Boundaries

### Process Mutation Safety: Observation vs Mutation
- **Observation / Discovery**: Non-destructive; uses `ProcessObservationSnapshotProvider`.
- **Destructive Mutation** (Kill, Suspend, Resume, Priority, Affinity, CPU Sets): **Cached observation NEVER authorizes mutation.** Before any mutation, freshly reacquire the targeted process and validate `PID`, `StartTime`, `ProcessName`, and executable identity/path where required. Fail closed on identity ambiguity.

### Companion Security
- Companion is transport/presentation only.
- Remote DTOs expose opaque server IDs and session tokens—never raw executable paths, PIDs, command lines, or credentials.
- Remote mutations require explicit write scopes (`read` does not imply `write`).
- Existing paired devices never silently gain new scopes; default pairing never silently grants strong new permissions.
- Authenticated write protection applies to loopback requests as well.

### Riot Games Conservative Policy
- Discovery via official Start Menu shortcuts only; launch via official shortcut only.
- Riot game, client, and Vanguard processes are **NEVER** suspended, terminated, reprioritized, pinned, or benchmarked.
- Strictly no memory inspection, injection, hooks, anti-cheat tampering, or private API exploitation.

### Hardware, PresentMon & Background Work
- Exactly one hardware monitoring backend (`HardwareMonitorService`). Sensors open only when `HardwareMonitorEnabled == true` and a consumer lease is active.
- Exactly one live PresentMon owner (`LivePerformanceTelemetryService`); preempted during benchmark capture.
- **Zero uncataloged timers**: Check [BACKGROUND-WORK.md](docs/architecture/BACKGROUND-WORK.md) before adding any loop or timer.

---

## 5. Development Workflow & Review Discipline

Agents must follow the 7-phase lifecycle detailed in [docs/agents/WORKFLOW.md](docs/agents/WORKFLOW.md):
1. **Phase 1: Baseline Verification**: Verify branch (`main`), commit HEAD, and clean worktree.
2. **Phase 2: Context Gathering**: Read relevant architecture docs and locate the authoritative owner.
3. **Phase 3: Implementation**: Narrowest coherent change, reuse existing owners, add focused tests.
4. **Phase 4: Self-Review**: Eliminate duplicate ownership and check for broken invariants.
5. **Phase 5: Validation**: Run all authoritative validation commands.
6. **Phase 6: Targeted Review**: If warranted or requested, execute ONE review using the P0–P3 rubric (see [docs/agents/REVIEW.md](docs/agents/REVIEW.md)). Perform at most **ONE narrow fix round**.
7. **Phase 7: Checkpoint**: Commit & push **ONLY** when explicitly requested by the user (see [docs/agents/GIT.md](docs/agents/GIT.md)).

---

## 6. Authoritative Validation Commands

Run these exact commands before declaring any task complete:

```powershell
# 1. Release build (0 warnings, 0 errors)
dotnet build --configuration Release

# 2. Desktop test suite (100% pass)
dotnet test FrameHub.Tests/FrameHub.Tests.csproj --configuration Release --no-build

# 3. Companion test suite (100% pass)
dotnet test FrameHub.Companion.Tests/FrameHub.Companion.Tests.csproj --configuration Release --no-build

# 4. JavaScript syntax validation across all frontend files
Get-ChildItem -Path "FrameHub.Companion/wwwroot/js/*.js" | ForEach-Object { node --check $_.FullName }

# 5. Diff hygiene (0 whitespace errors)
git diff --check

# 6. Status and diff statistics
git status --short --untracked-files=all
git diff --stat
```

---

## 7. Git Safety & Interrupted Handoff Rules

- **Default State**: Leave work **UNCOMMITTED** and **UNSTAGED** unless the user explicitly asks for a commit or checkpoint.
- **Never Casually Execute**: `git push --force`, `git commit --amend`, `git rebase`, `git clean -fd`, `git reset --hard`, `git restore .`, `git stash`, or branch switching.
- **Dirty Worktree Safety**: If a clean baseline is required and uncommitted changes exist: **STOP** and report `BLOCKED BY DIRTY WORKTREE`. Never auto-clean.
- **Interrupted Agent Handoff**: If continuing work from a dirty worktree left by a previous agent:
  1. Confirm the baseline commit hash did not drift.
  2. Inspect all tracked and untracked changes (`git status`, `git diff`).
  3. Preserve valid partial work—do not restart from scratch or rewrite working code.
  4. Complete the remaining requirements and run full validation.

---

## 8. Ambiguity & Conflict Resolution Discipline

- **No Invented Decisions**: If a product requirement or UX behavior is ambiguous, do NOT silently invent a decision. Check canonical docs (`docs/ROADMAP.md`, `docs/architecture/*`). If still unresolved, stop and present the options/tradeoffs, or mark with an explicit `TBD`.
- **Conflicting Documentation**: If two documents conflict, do not silently pick one. Check repository code evidence to identify the true intended invariant, or escalate for user clarification.
