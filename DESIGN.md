# FrameHub desktop design — work in progress

This document records the elements currently implemented in the WPF app. It is
not a claim that the 3.0 redesign is complete.

## Direction

FrameHub is an operating tool: the current task, real system state and recovery
actions lead. The shared surface uses cool graphite with a warm brass action
accent. Status colors remain distinct from the action color. Keep data honest:
an unavailable measurement is a state, never a fabricated value.

## Tokens and shared controls

The authoritative tokens are in `FrameHub.App/Themes/Colors.xaml` and
`Typography.xaml`: background `#10151B`, sidebar `#151C23`, raised surface
`#202C36`, primary text `#F2F4F2`, secondary text `#C4CFD0`, action
`#EAB45D`, and focus `#FFE1A5`. Segoe UI is the system text face; Consolas
is used for data and logs. WPF device-independent units and the existing
spacing tokens are used throughout.

Shared button, focus, navigation, state, surface and table styles live in
`FrameHub.App/Themes/Controls.xaml`. Tables keep their own finite scrolling
viewport and column minimums; do not wrap the Processes or Profiles grids in an
unbounded horizontal scroller. Keep focus distinct from selection, use text in
addition to color for state, and avoid decorative movement.

## Implemented compositions

- The shell exposes the current page in the title bar and session/profile status
  in the navigation area. Its existing compact rail remains the navigation
  mechanism below the shell breakpoint.
- The Dashboard groups session and game selection, separates system state from
  benchmark status, and reflows the state band from four columns to two.
- Library onboarding separates the principal scan action from source-specific
  actions and removes the enclosing empty data panel.
- Session controls precede the rules and preview in the visual and keyboard
  order. The controls stack when there is insufficient width.
- Logs use actual rows rather than a decorative ruled background.
- Processes and Profiles give the list the available width until an item is
  selected. The inspector opens alongside it on wide windows and below it on
  compact windows; each pane keeps its own finite scroll viewport.
- Hardware's disabled state and background behavior share a responsive overview
  rather than full-width stacked bands. No sensor reading is synthesized.
- Settings groups the selected category in a distinct reading surface while
  retaining the side index or compact category selector.

Other views still need their intended structural redesign and verification.
The resources alone do not constitute a complete design system migration.
