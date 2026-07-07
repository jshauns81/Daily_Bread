# Screen-Time UI — build log & checklist

**Goal:** surface the already-built screen-time / importance *engine* in the app UI,
**in the current visual language** — no recolor, no planner restyle. Just the controls
and displays that make the engine usable and tunable.

**Branch:** `feat/screentime-ui` (off `origin/beta`). Ship target: merge to `beta`, then
`beta → master` when ready. **Do not** pull in the `redesign/ultraviolet` recolor/layout.

**Scope decisions (locked):**
- **Single child only.** Build nothing multi-child. Resolve the one `ChildProfile`
  directly — no picker, no per-child loops, no "which kid" affordances. (Removing the
  *existing* multi-child code is a SEPARATE future task, deliberately not done here.)
- **Time Machine (MECHANICS_AMENDMENT §F) is parked** — its `FixRequest` backend was
  never built, even on `beta`. Out of scope for this effort.
- Live "≈N min if missed" price hint on the chore form is deferred to the meter.

---

## What already exists (the engine — do NOT rebuild)

On `beta`, live and wired (`Program.cs` registers all of these + a weekly hosted reconciler):
- Services: `ScreenTimePricingService` (`GetWeekPricingAsync` → `WeekPricing`/`ChorePrice`),
  `WeeklyReconciliationService` (+ `WeeklyReconciliationHostedService`), `QolShareService`,
  `QolRebalancer`.
- Entities: `ScreenTimeEntry`, `QolShare`, `ChildWeeklyScreenTimeBudget`, `ChoreScreenTimeState`.
- `ChoreDefinition.Importance` (0–10), `.AllOrNothing`, `.IsInverseFill`.
- `ChildProfile`: `WeekdayScreenTimeHours` (40), `WeekendScreenTimeHours` (20),
  `WeekdayAtRiskPercent` (30), `WeekendAtRiskPercent` (50), `WeeklyRoutinePayout`.
- `ChoreDefinitionDto` already carries `Importance` + `AllOrNothing`; `ChoreManagementService`
  already **validates** Importance 0–10 and persists it.
- **No DB migration needed for any of Slice 1** — every field already exists.

The engine is effectively dormant only because nothing sets Importance (no form input) and
nothing displays the result (no meter). Slice 1 turns it on.

---

## Checklist

### Slice 1 — make the engine real & tunable
- [ ] **Piece 1 — Importance input on `ChoreForm`** *(small; data path already exists)*
  - [ ] Add `Importance` (int, default 0) to `ChoreFormModel`
  - [ ] Load mapping (Chore DTO → FormModel) + save mapping (FormModel → DTO)
  - [ ] 0–10 control in `ChoreForm.razor`, current `ds-input` look, `0 = no screen-time impact`
  - [ ] Round-trip test (create + edit preserves Importance)
- [ ] **Piece 2 — Single-child screen-time settings** *(small–medium)*
  - [ ] "Screen Time & Pay" section on `Settings.razor` for the one child
  - [ ] Inputs: weekday/weekend pool hours, weekday/weekend at-risk %, `WeeklyRoutinePayout`
  - [ ] `ChildProfileService.UpdateScreenTimeSettingsAsync` (validated)
  - [ ] Save test
- [ ] **Piece 3 — Kid screen-time meter** *(medium)*
  - [ ] New `ScreenTimeMeter.razor` (current look), placed on kid branch of `Home.razor`
  - [ ] Reads `GetWeekPricingAsync` + latest `ChildWeeklyScreenTimeBudget` snapshot
  - [ ] Live "pending −N min this week" (reuse pricing/reconciliation math read-only)

### Slice 2 — complete it (after Slice 1 feels right)
- [ ] All-or-nothing threshold toggle on WeeklyFrequency earning chores
- [ ] QOL mixer bar (draggable shares; `QolRebalancer` math already exists)
- [ ] Kid "at-risk today" card (MECHANICS_AMENDMENT §E escalation)

### Parked (separate tasks)
- [ ] Multi-child removal (single-child-only cleanup across the app)
- [ ] Time Machine (§F) — needs `FixRequest` backend built first

---

## Verify
`dotnet build Daily_Bread/Daily_Bread.csproj` (0 errors) +
`dotnet test Daily_Bread.Tests/Daily_Bread.Tests.csproj` (baseline 106 green — keep green).
Smoke: set Importance on a chore → set a pool in settings → kid meter shows pending minutes.

## Status log
- 2026-07-07 — Branch `feat/screentime-ui` created off `beta`. Plan + scope locked. Starting Piece 1.
