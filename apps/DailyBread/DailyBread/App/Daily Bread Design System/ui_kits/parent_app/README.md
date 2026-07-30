# UI kit — Parent app (iPhone)

Recreated from `DailyBread/DailyBread/Features/Home/ParentHomeView.swift`,
`Features/Approvals/ApprovalsView.swift`,
`Features/Planner/{PlannerView,PlannerGridView,ChoreEditorSheet}.swift`,
`Features/Settings/SettingsView.swift`, `Components/SheetKit.swift`, and `App/MainView.swift`.

## Files
- `index.html` — the interactive kit: four tabs, drill-in, two working sheets.
- `ParentScreens.jsx` — `ParentHome`, `ParentApprovals`, `HelpRespondSheet`,
  `ParentPlanner`, `ChoreEditorSheet`, `ParentChildDay`.
- Also loads `../kid_app/KidScreens.jsx` for the shared `AppSettings` screen.

## What you can do
- **Approve** one chore → the row washes gold (`glow @18%`) for 0.9s, then leaves the
  queue and the tab badge drops. **Approve all (5) — $12.50** morphs *in place* into its
  own confirm row; there is no system dialog anywhere in this app.
- **Expand** either queue past its 3-item preview with the inline "N more waiting ⌄" row.
- **Open a Help request** → the three-outcome sheet, in the family's own words:
  ✓ Fulfill for them (gold) · Grant dispensation · ↺ They must try again.
- **Planner** → toggle any grid cell to schedule a day; switch to the list view; tap a
  chore to open the full editor (identity, Earns/Expected, schedule rule, screen-time
  importance, options) built entirely from `SheetKit`.
- **Tap Ada's row on Home** → drills into her day using the *kid's own Today screen*,
  with Help hidden — exactly as `TodayView(userId:)` does.
- **Settings** → six themes, live, plus the family feature switches.

## Shell geometry (from MainView.swift)
iPhone is a `TabView` with four tabs and a badge on Approvals equal to
`pendingApprovals + helpRequests`. macOS uses a `NavigationSplitView` sidebar titled
"Daily Bread" driven by the same `Section` enum — not recreated here, since the tab
version carries the same content.

The parent's iOS Home **hides the navigation bar** and draws its own header: "Home" in
`largeTitle.bold` with the calendar and gift icons baseline-aligned beside it.

## Single-child handling
The kit runs with one child, and every branch the source documents is honoured:
the balance section header reads **"Balance"** (singular), the approval rows **omit the
child's name**, the planner shows **no child filter**, the chore editor has **no "Who's
it for" card**, and Settings reads **"Show goals to Ada"**. Add a second child to the
`DASH0` array in `index.html` and all five flip.

## Not recreated
Calendar, Ledger detail, Reward Claims, Achievement definitions, Family members,
Driving approvals, the Adjust-balance and Screen-time-settings sheets, and the macOS
split view. All are listed under "Known gaps" in `readme.md`.
