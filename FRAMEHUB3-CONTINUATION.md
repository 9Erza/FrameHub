# FrameHub 3.0 continuation

Status: **incomplete; not a release**. Worktree is `C:\1_eryk_prywatne\Projekty\FrameHub-3`, branch `codex/framehub-3`, originating at `76d0b80a93a5a2520ab0bab0511f08b9c784b909`. Continue in this worktree and inspect its current HEAD and status before editing. No domain owners were added.

## Implemented so far

- Shared graphite/brass resources and data-grid styling in `Themes`; shell title and session/profile state; existing rail retained.
- Dashboard groups session/game choice and places system state in a four/two-column band. Localized `SystemOverviewTitle` was added.
- Library empty state removes the enclosing data panel and keeps existing scan/add commands. Session puts game/session controls before preview/rules in both visual and keyboard order; manual game selection no longer requires a 320-DIP column.
- Logs use real rows without a decorative ruled background. Benchmark capture removes duplicate no-game panels and gives the game selector the available width.
- Profiles and Processes now give their tables the full workspace until selection; the inspector opens beside the list on wide windows and in a finite, vertically scrollable workspace on compact windows. Hardware places its OFF and background behavior panels side by side when space permits. Settings has a category index and distinct content surface. The narrow rail no longer loses icon width to the scrollbar, and the compact Dashboard makes its primary action visible at 880×460.
- `DESIGN.md` describes the implemented subset, not a finished design system.

## Verification and remaining work

- Release build after the latest UI edits succeeded with 0 warnings/errors. The executable is `FrameHub.App\bin\Release\net10.0-windows\FrameHub.App.exe`.
- The previous baseline Desktop run was 794/797: `LibraryFilter_Cs2_UsesShortcutIdentity`, `LiveService_TracksCorrectPid_AndCalculatesRollingMetrics`, and `ChangingPortWhileRunning_MovesListenerToNewPort` failed before UI edits. The latest full run on the current build was 796/797, with only the same `LiveService_TracksCorrectPid_AndCalculatesRollingMetrics` failure. Companion passed 141/141; JavaScript syntax and diff whitespace checks passed.
- `OverhaulCScreenshots.RenderAfterScreenshots_EnAndPl` passed within the latest full run. Its 20 real offscreen WPF renders with synthetic data and two actual window captures are copied to `C:\1_eryk_prywatne\Projekty\FrameHub-3-evidence-session3`. Offscreen captures omit the shell and are not real DPI tests. The bare-view render does not pump the dispatcher; profile columns can appear clipped there. Verify Processes and Profiles across populated states, horizontal scrolling and the full 880–1540 width matrix before claiming the F8/F9/N1/N2 regressions are absent.
- Actual isolated-app visual checks covered Dashboard, empty Library, Session, Processes, Profiles, Hardware and Settings in English. Profiles were also checked with two disabled synthetic profiles at 1540×900, 880×640 and 880×460. At minimum size selection brings the editor into view; its lower actions were reached by scrolling. The data grid keeps a finite viewport and its own horizontal scrollbar. The synthetic capture is `window-profile-synthetic-880x460-editor.png` in the evidence directory. This is not a complete F8/F9/N1/N2 matrix. No actual-window PL or DPI matrix was completed.
- Full structural work remains for populated Library and its inspector, benchmark history/compare, Logs, dialogs and remaining states; Processes, Profiles, Hardware and Settings need further refinement and state coverage. The current result does **not** satisfy the user's full redesign brief; preserve all commands and safety semantics while completing it. Review all screens at real target sizes, both languages and accessible keyboard states; avoid real process mutations during visual checks.
- Run the exact six validation commands in `AGENTS.md` after the last edit, review changes, and commit logical verified work only on this branch. Never merge into `main` without the owner's separate request.

## Structural checkpoint — 2026-10-27

This remains incomplete. Starting HEAD for this continuation was `50acb99`.
The shell removes duplicated title/status chrome, consolidates project links in
an expandable sidebar footer, and tightens navigation selection/focus. Shared
process table cells now align with headers; numeric columns align right and the
priority column accommodates its contents. Settings uses a left-aligned category
workspace with diagnostics confined to Advanced. Session leads with manual start;
Dashboard leads with game selection. Keyed runtime activity entries retranslate
on language change; unkeyed historical text remains in its original language.
Existing owners and commands were reused. Commits `1fd2ecc`, `d045a60`,
`26a9c6f` and `cdcd758` contain this work.

Actual isolated-app captures are in
`C:\1_eryk_prywatne\Projekty\FrameHub-3-structural-evidence-20261027`.
They cover selected EN/PL Dashboard, Processes, Settings and Session states at
approximately 1540x900 and 880x460; they do not establish the full language,
DPI, keyboard, state or width matrix. No actual 1100x700 or 1280x800 capture
was made in this pass. Remaining work includes populated Library, benchmark
history/compare, Logs, dialogs, other states and comprehensive regression and
accessibility verification. Do not describe F8/F9/N1/N2 as fully verified.

The user's existing FrameHub instance locks the standard Release output. Keep it
running; build and tests can use `-p:OutDir=C:/1_eryk_prywatne/Projekty/FrameHub-3/artifacts/structural-build/`.
A Release build there passed with zero warnings and errors. The normal exact
Release build is blocked by the locked output while that instance remains open.
The earlier verification details above describe the prior checkpoint, not this one.

With isolated data directories and this output path, the final Desktop suite passed
799/799, Companion passed 141/141, and JavaScript syntax validation passed.
The sidebar regression test was updated for the footer expander while retaining
the rail visibility and drawer-command checks.

## Next implementation checkpoint — 2026-10-27

This phase is partial, not the requested completed redesign. Work started clean
at `e3d10da` on `codex/framehub-3`. Three presentation commits were made:

- `3d988ab` changes the populated Library from action-heavy cards to selectable
  rows with a contextual inspector, keeps existing commands, and fixes the stale
  no-profile explanation when a game is selected. It is not a full Library
  reconstruction. The existing `IsReady` definition can report an item ready
  when its configured executable is missing; review its product semantics before
  changing it. Verify long names, filter/selection behavior, PL and more sizes.
- `cd0faf7` improves benchmark metric wrapping, compact history proportions,
  comparison alignment and the selected-session label in the ComboBox. This is
  not the full requested analytical redesign. Check the empty chart when raw
  samples are unavailable, responsive compare and all capture states.
- `ea1adc8` adds compact level/time rows and a selectable, scrollable full-message
  area to Logs. Filters and the existing clear confirmation remain in place.
  Test long messages, levels, empty results and both languages at small sizes.

Real WPF captures live outside the worktree at
`C:\1_eryk_prywatne\Projekty\FrameHub-3-next-evidence`. Library uses 24
synthetic items; benchmark history/comparison uses two synthetic summaries (no
raw frames). Captures were checked in English at approximately 1540x900, and
Library selection at approximately 880x460. The captures are not evidence of a
complete PL/EN, width, DPI, accessibility, or state matrix. Do not claim full
visual acceptance for any area. Existing user process was left running.

The dialog inventory found `ModernDialogService`, `UpdateAvailableDialog`,
benchmark and other system message boxes, and native file/folder pickers. No
dialog redesign or complete global consistency pass was performed. Finish the
Library, Benchmarks, Logs and dialogs in the original priority order. Keep both
`FRAMEHUB_DATA_DIR` and `FRAMEHUB_LOCAL_DATA_DIR` isolated for future UI tests;
benchmark storage is under the latter.

Final validation for this checkpoint: Release build with OutDir
`artifacts/next-phase/` passed with 0 warnings and errors. With isolated roaming
and local data roots, Desktop passed 800/800 and Companion passed 141/141;
JavaScript syntax and diff whitespace checks passed. The exact standard Release
build was attempted and failed with locked output DLLs held by the existing
FrameHub.App process 19316; the exact standard no-build tests were not used as
proof of the changed binaries. No changes to that process were made.
