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
