# Chore System Redesign — Screen-Time Penalties & Flat Routine Payout

> **Handoff doc.** Design conversation captured for continuation in a fresh environment.
> Project: `Daily_Bread` (.NET Blazor). Branch at time of writing: `security-cleanup-v3`.
> Status: **money model fully locked**; **all 5 screen-time questions now answered** (see §5). Ready for implementation.
> Child referenced throughout: "Kid."

---

## 1. Goal

Rework how chores drive consequences. Today the only levers are money (tasks pay,
routines don't) and a "stern talking-to." In practice the thing Kid actually cares
about is **screen time**. So the redesign:

- **Removes all financial penalties.** Money is now earn-only — he can only ever *fail to
  earn*, nothing is ever deducted from his balance.
- **Makes screen time the sole real penalty axis.** Missing chores reduces his weekly
  screen-time (ST) budget, with week-over-week compounding.
- **Adds "vacuum-fill" routines** whose targets grow as ST is lost (read / active / brain).

---

## 2. How the current system is modeled (code grounding)

| Concept | Where | Notes |
|---|---|---|
| Chore definition | `Daily_Bread/Data/Models/ChoreDefinition.cs` | `EarnValue` (>0 = "Task"), `PenaltyValue` (routines/expectations), `IsEarning`/`IsExpectation` helpers, `ScheduleType` (`SpecificDays` daily-fixed vs `WeeklyFrequency` X-times/week), `ActiveDays` flags, `WeeklyTargetCount`, `AutoApprove`. |
| Per-date status | `Daily_Bread/Data/Models/ChoreLog.cs` | One row per (chore, date). `ChoreStatus`: `Pending/Completed/Approved/Missed/Skipped/Help`. `Missed` set when still `Pending` at end of day. Has optimistic-concurrency `Version`. |
| Global settings | `Daily_Bread/Data/Models/FamilySettings.cs` | `DailyExpectationPenalty` ($0.10), `WeeklyIncompletePenaltyPercent` (10%), `WeekStartDay` (Monday), `CashOutThreshold`, VAPID push keys. **Family-global (single family today).** |
| Child data | `Daily_Bread/Data/Models/ChildProfile.cs` | Per-child — natural home for per-child ST + payout settings. (Already being edited on this branch.) |
| Week-end processing | `Daily_Bread/Services/WeeklyReconciliationService.cs` | Natural home for the compounding math. |
| Help resolution | `Daily_Bread/Services/TrackerService.cs` (`RespondToHelpRequestAsync`, ~L830-860) | `CompletedByParent → Approved`, `Excused → Skipped`, `Denied → Pending` (help fields cleared, so end-of-day sweep can mark `Missed`). |
| Planner UI | `Daily_Bread/Services/ChorePlannerService.cs` + planner Razor components | Where the ST slider/box goes at the top. |

**Key mapping of your terms:**
- **"Task"** = `ChoreDefinition` with `EarnValue > 0` (`IsEarning`).
- **"Routine"** = `ChoreDefinition` with `EarnValue == 0` (`IsExpectation`; `PenaltyValue` was the money side).
- Screen time is a **brand-new orthogonal axis** — it does not disturb the earning logic.

---

## 3. LOCKED DECISIONS — Money model

### 3.1 No financial penalties, ever
He can only fail to *earn*. Nothing is deducted from his balance.

- **Tasks** (individual `EarnValue`): do it → paid that amount; don't → $0. No penalty on top.
  (Already how earning works — just delete the penalty machinery.)
- **Routines**: collectively worth a **flat weekly pool** (default **$10**), split into equal
  slices. Complete a routine instance → earn its slice; miss it → forfeit that slice.
  Independent of task earnings (two separate money streams).
- **Screen time** is now the only "penalty."

### 3.2 Flat routine payout — per-instance slices
- **UI:** a single **fill box** "Weekly routine payout = $10.00" (per-child, configurable).
  **No slider** for this one.
- **Slice value = `$10 ÷ (routine instances scheduled that week)`.**
  Example: 10 routines × 7 days = 70 instances → ~$0.14 each. Miss one → 14¢ off.
  Denominator floats with the actual schedule, so the ceiling is always exactly $10.
  No per-routine weighting ("no routine worth more than another"), nothing to game.
- **Computation nuance (important):** compute payout as **`$10 × (credited ÷ total)`**,
  NOT as a sum of rounded 14¢ slices (70 × $0.14 = $9.80 would never hit a clean $10).
  The 14¢ figure is only how we *explain* it to Kid; the math stays exact.

### 3.3 Credit rules (Help / excuse flow)
"All parentally applied chores are as if he did it," with one variation for denied help.
The current Help flow already does most of this:

| Parent action | Status result | Counts as credit (slice paid)? | ST hit? |
|---|---|---|---|
| Completed-by-parent | `Approved` | ✅ yes (already) | no |
| Excused | `Skipped` | ⚠️ **CHANGE:** must now pay the slice ("as if he did it") | no |
| **Denied help** | `Pending` (help fields cleared) | falls back to a normal chore; if still not done → `Missed` | — |
| Not done / help denied then not done | `Missed` | ❌ no slice | **yes** |

**The single semantic change:** today `Skipped` = *"no penalty, no earning."* Under the new
model an **Excused/Skipped routine pays its slice and takes no ST hit** (stays in the
numerator and denominator). `Denied → not done → Missed` correctly forfeits the slice — this
is already your intended "one variation" and is already implemented via
`HelpResponse.Denied → Pending`.

### 3.4 Code changes implied by the money model
- ❌ **Delete:** `ChoreDefinition.PenaltyValue`, `FamilySettings.DailyExpectationPenalty`,
  `FamilySettings.WeeklyIncompletePenaltyPercent`.
- ⚠️ `IsExpectation` (currently `EarnValue == 0 && PenaltyValue > 0`) no longer valid —
  routines are no longer defined by penalty money.
- ➕ **Add explicit `ChoreKind { Task, Routine }`** enum on `ChoreDefinition` (stop inferring
  type from dollar values; this is the field the payout logic keys off).
- ➕ **Add per-child `WeeklyRoutinePayout`** (default $10) — likely on `ChildProfile`.
- 🔧 Make `Excused/Skipped` credit the routine slice and suppress any ST hit.

---

## 4. LOCKED DECISIONS — Screen-time mechanics

### 4.1 The two ST budgets (sliders at top of planner)
Reconciliation of the two framings (they agree):
- **Weekday pool** e.g. **40h ÷ 5 weekdays = 8h/day** ← matches "8 hrs a day"
- **Weekend pool** e.g. **20h ÷ 2 weekend days = 10h/day**

Store the two **pool totals** (that's the mental model of the slider); render the per-day
rate ("8 → 7") as derived display. **Per-child** (hang off `ChildProfile`), not
`FamilySettings`.

### 4.2 Subtractor → time mapping
Each chore gets a `ScreenTimeSubtractor` (int 0–10; 0/blank = no ST impact).
**Anchor: 10 = 1 hour.** So `minutes_lost = Subtractor × 6`.

| Subtractor | ST lost |
|---|---|
| 0 / blank | none |
| 5 | 30 min |
| 10 | 60 min |

Applies to **any** chore (Task or Routine) with a subtractor > 0.

### 4.3 Compounding (consecutive weekly misses of the same chore)
Reverse-engineered from the stated example (base = 10) — matches exactly:

| Consecutive weeks missed | Cumulative multiplier | Value (base 10) | Step |
|---|---|---|---|
| 1 | ×1.00 | 10.00 | — |
| 2 | ×1.50 | 15.00 | +50% |
| 3 | ×2.625 | 26.25 | ×1.75 of prior |
| 4 | ×5.25 | 52.50 | ×2 of prior (doubles) |

Pattern: each additional week multiplies by a factor that itself grows — **1.5× → 1.75× →
2×** — and a full doubling is the ceiling on the *growth rate*. Total tops out at **×5.25**.

⚠️ **Consequence to keep in mind:** at ×5.25 a single `Subtractor=10` chore missed 4 weeks
straight removes **5.25h** — over half an 8h weekday. A few neglected chores could zero out
ST. Intended deterrent, or add a floor? → open question.

### 4.4 Data model additions implied by ST
- ➕ `ChoreDefinition.ScreenTimeSubtractor` (int 0–10, default 0)
- ➕ `ChoreDefinition.IsInverseFill` (bool) + `InverseFillBaselineMinutes` (int) for
  vacuum-fill routines (read / active / brain)
- ➕ Per-child `WeekdayScreenTimeHours`, `WeekendScreenTimeHours` (the two sliders)
- ➕ Persist per-(chore, child) **consecutive-miss streak** + resulting weekly ST reduction —
  compute in `WeeklyReconciliationService`.

### 4.5 Vacuum-fill (inverse) routines
As ST is lost, certain routines' targets grow to "fill the vacuum."
Formula: **`added_minutes_per_routine = total_ST_lost ÷ count(inverse routines)`.**
Example: lose 1h across 3 inverse routines → **+20 min each** (the "33.3%" = each routine's
*share of the vacuum*, not a 33% bump). Requires giving those routines a **target duration**
(they currently have none — just done/not-done). → enforcement is an open question.

---

## 5. RESOLVED DECISIONS — Screen-time (answered 2026-07-06)

1. **Cap:** ✅ Freeze compounding total at **×5.25**; hold there every week after.
2. **Floor:** ✅ **Max 70% ST loss per week**, per day-type pool. Total weekly reduction
   for a given pool (weekday / weekend) can never exceed 70% of that pool.
3. **Reset rule:** ✅ Completing a chore **resets that chore's streak to 0** (next miss
   starts back at ×1.0). No gradual cool-down.
4. **Daily cadence:** ✅ **Option A (per-occurrence).** Each missed day = `Subtractor × 6`
   min, summed across the week, then the week's total gets the streak multiplier.
5. **Vacuum-fill enforcement:** ✅ **Soft target (display only)** for v1 — show the inflated
   target, never auto-fail. Hard requirement deferred.
   - ⚠️ **Still needed:** baseline minutes for Read / Active / Brain. Scaffolding with a
     **default of 20 min each** (`TODO` — tune later; harmless while display-only).

---

## 6. Recommended data-model change summary (once questions answered)

```
ChoreDefinition:
  - REMOVE PenaltyValue
  + ADD ChoreKind Kind            // Task | Routine  (replaces EarnValue/Penalty inference)
  + ADD int ScreenTimeSubtractor  // 0–10, default 0
  + ADD bool IsInverseFill
  + ADD int  InverseFillBaselineMinutes

ChildProfile (per-child settings):
  + ADD decimal WeeklyRoutinePayout      // default 10.00
  + ADD decimal WeekdayScreenTimeHours   // e.g. 40 (pool) or 8 (per-day) — TBD storage unit
  + ADD decimal WeekendScreenTimeHours   // e.g. 20 (pool) or 10 (per-day)

FamilySettings:
  - REMOVE DailyExpectationPenalty
  - REMOVE WeeklyIncompletePenaltyPercent

New persistence (per chore+child, weekly):
  + consecutive-miss streak counter
  + computed weekly ST reduction (with compounding + cap + floor)

WeeklyReconciliationService:
  + tally misses → apply per-instance/streak logic → compound (cap ×5.25, floor) →
    set next week's ST budget → recompute inverse-fill targets
  + routine payout = WeeklyRoutinePayout × (credited instances ÷ total scheduled instances)
    where credited = Completed | Approved | Skipped(excused)
```

---

## 7. Implementation status (backend "underlayment" done 2026-07-06)

Phases 1–4 (everything except the Planner visual UI) are implemented, building clean
(0 warnings), 59 tests passing. The Planner UI (§Phase 5) is deliberately left for the
Claude-Design instance. **The C# surface the UI binds to already exists** — build against
these names:

**New model fields**
- `ChoreDefinition.Kind` (`ChoreKind { Task, Routine }`), `.ScreenTimeSubtractor` (int 0–10),
  `.IsInverseFill` (bool), `.InverseFillBaselineMinutes` (int, default 20). `PenaltyValue` removed.
- `ChildProfile.WeeklyRoutinePayout` (default $10), `.WeekdayScreenTimePoolHours` (40),
  `.WeekendScreenTimePoolHours` (20) — pool totals; per-day is derived display.
- `FamilySettings` penalties removed.
- New entities: `ChoreScreenTimeState` (per chore+child streak), `ChildWeeklyScreenTimeBudget`
  (per child+week snapshot the UI reads for "next week you have N hrs").
- `ChoreDefinitionDto` carries `Kind`, `ScreenTimeSubtractor`, `IsInverseFill`,
  `InverseFillBaselineMinutes`; `TrackerChoreItem` carries `Kind`, `ScreenTimeSubtractor`.

**Migration:** `20260706025511_AddScreenTimeAndChoreKind` — backfills `Kind` from `EarnValue`,
seeds existing children with the $10 / 40h / 20h defaults. **Not yet applied to any DB.**

**Reconciliation** (`WeeklyReconciliationService`): routine pool payout
(`$10 × credited ÷ total`, exact fraction), per-occurrence ST tally, compounding ×1→×1.5→
×2.625→×5.25 (capped), 70% per-pool floor, inverse-fill share, idempotent per week.

### Planner UI (Phase 5) — remaining, for the design instance
Two ST sliders (bind `WeekdayScreenTimePoolHours` / `WeekendScreenTimePoolHours`), one payout
fill box (`WeeklyRoutinePayout`), a per-chore subtractor field (`ScreenTimeSubtractor`) + an
inverse-fill toggle (`IsInverseFill` / `InverseFillBaselineMinutes`) in the chore form.
Only `ChoreForm.razor`, `ChorePlanner.razor`, `Tracker.razor`, `ManageChores.razor` got
minimal compile-keeping edits — safe to restyle freely.

### Still open / to tune
- Baseline minutes for Read / Active / Brain inverse routines (scaffolded at 20).
- Pool-attribution of a missed occurrence uses the missed day's day-type (weekday vs weekend);
  revisit if a different mapping is wanted.
