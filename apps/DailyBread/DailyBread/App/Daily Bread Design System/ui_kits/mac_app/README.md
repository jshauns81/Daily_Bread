# UI kit — macOS (NavigationSplitView)

Recreated from `DailyBread/DailyBread/App/MainView.swift` — the same `Section` enum
that drives the iPhone `TabView` drives this sidebar, which is why the kid and parent
shells are one component with two lists.

## Files
- `index.html` — interactive: switch between the parent and kid account, click every
  sidebar section, drill into a child's day, open the Calendar / Reward Claims /
  Achievements / Family / Driving-log detail screens.
- `MacShell.jsx` — `MacWindow` (sidebar + detail + toolbar), `ToolbarButton`,
  `PARENT_SECTIONS`, `KID_SECTIONS`.
- Reuses `../kid_app/KidScreens.jsx` and `../parent_app/ParentScreens.jsx` unchanged —
  **the screens are not re-authored for macOS**, they just get a wider column. That is
  the single-codebase claim made visible.

## What macOS changes, and all it changes
- Tabs become a **sidebar** (`--ds-mac-sidebar`, 264px) with traffic lights above it and
  the selected row filled with the accent.
- The Approvals badge is a **plain trailing number**, not the red pill iOS uses.
- The header gains a rounded-capsule toolbar cluster; iOS puts those icons bare beside
  the large title.
- Content is capped at `--ds-mac-content` and centred, so cards never stretch to a
  30-inch display.
- Everything else — cards, rows, sheets, spacing, motion, semantics — is identical.

## Not recreated
Real window resizing/traffic-light behaviour, the iPad three-column layout, and menu-bar
commands. Sheets present as centred overlays rather than macOS sheets.
