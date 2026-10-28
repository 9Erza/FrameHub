# FrameHub.GameData — dormant research data

> **Not used by FrameHub 1.0.0.** Production does not reference this data set:
> it has no `.csproj`, is absent from `FrameHub.slnx`, and neither the
> WPF app nor Companion calls `GameDataService` / `GameDataModels`.
> The active Counter-Strike 2 workflow uses `Cs2OptimizationService`.

This directory retains **experimental declarative data** for a possible
future game-integration architecture. It is not packaged as an active
configuration source, and editing these JSON files will not change
FrameHub's current game settings or optimization behavior.

Currently retained research data: `games/counter-strike-2/`.

**Do not** put executable scripts, PowerShell, batch files, DLLs, binary
patches or credentials in this data directory.

The possible adopt-or-remove decision is deliberately deferred to a separate
reviewed change: see
[`docs/architecture/REFACTOR-CANDIDATES.md`](../docs/architecture/REFACTOR-CANDIDATES.md),
[`docs/architecture/OVERVIEW.md`](../docs/architecture/OVERVIEW.md) and
[`docs/audits/2026-10-24-noc-functional-inventory.md`](../docs/audits/2026-10-24-noc-functional-inventory.md).

A future implementation would need an explicit production consumer, packaging
contract, tests, security review and documentation update. Nothing in this
directory should be interpreted as a shipping feature.
