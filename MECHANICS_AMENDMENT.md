# MECHANICS AMENDMENT — for CHORE_SCREENTIME_REDESIGN.md

> Amends the design-conversation spec after a UX review pass (July 2026).
> §3 (money model) is UNCHANGED and stays locked. This document **replaces §4.2, amends
> §4.3–4.5 and §5**, and adds three new mechanics: threshold pay w/ redemptive reps,
> the at-risk escalation rule, and the Time Machine. Where this doc and the original
> conflict, this doc wins.

---

## A. REPLACES §4.2 — Importance shares (not fixed minutes)

The `ScreenTimeSubtractor` (10 = 1 hour, `minutes = subtractor × 6`) is **dropped**.
Fixed per-chore minutes make total exposure scale with chore count, which forced the
70%-loss cap as a patch. Replace with relative **importance shares** against a fixed
per-pool penalty budget:

- `ChoreDefinition.Importance` — int 0–10 (0/blank = no ST impact). Set by parents
  ("how important is this?"), not a time value.
- Per pool (weekday / weekend), per child: `AtRiskPercent` — the maximum share of that
  pool that can be lost in a week. **Defaults: weekday 30%, weekend 50%.** The floor
  (100% − AtRiskPercent) is structural: the guaranteed zone in the UI.
- **Cost of one missed instance:**
  `loss = (Importance ÷ Σ Importance of all instances scheduled that week in that pool) × PoolBudget`
  where `PoolBudget = PoolHours × AtRiskPercent`.
- Adding chores never inflates total exposure — shares renormalize. Miss everything =
  lose exactly the budget, never more. This REPLACES the §5.2 cap (it is now inherent).
- UI always displays **minutes, never the importance number or multiplier** — since the
  weekly schedule is known, every chore shows a live price ("Yard: 45 min if missed").

## B. AMENDS §4.3 — Compounding

- Streak multiplier applies to **that chore's share**, then the pool total is clamped at
  the pool budget. Keep the agreed curve (×1.0 / ×1.5 / ×2.625 / ×5.25, frozen at ×5.25)
  or simplify to ×1.0/×1.5/×2.0/×3.0 — implementer's choice, but the UI contract is the
  same either way: show minute prices, plus "next week this drops back to N min" when a
  streak is alive.
- §5.3 unchanged: completing the chore resets its streak to 0. Surface this as a positive
  event (achievement-style toast), not silently.
- §5.4 unchanged (per-occurrence, summed, multiplier on the week's sum) — which means the
  running weekly total is computable mid-week: **show a live "pending −N min this week"**
  accrual on the kid's meter; apply officially at reconciliation. No Sunday surprises.

## C. AMENDS §4.5 + §5.5 — Vacuum-fill (QOL) distribution

- QOL routines (Read / Active / Brain, extensible) get **explicit shares that always sum
  to 100%** (replaces equal division). Parent UI = stacked mixer bar: dragging one
  segment rebalances the others **proportionally to their current ratios**; a per-routine
  **lock** exempts it from rebalancing; snap to 5%; a new QOL routine enters at 0%.
  - Rebalance algorithm: on segment i changing by Δ, distribute −Δ across unlocked j≠i as
    `Δj = −Δ × (sharej ÷ Σ unlocked shares excluding i)`; clamp at 0; if all others locked, the drag is blocked.
- `added_minutes(routine) = AppliedWeeklyLoss × share`. **AppliedWeeklyLoss is the
  post-clamp (post-budget) loss**, not the raw sum — otherwise a blowout week creates
  absurd reading targets.
- Baselines stay (default 20 min each, display-only soft targets per §5.5).
- **NEW — Earn-back:** completing a boosted QOL routine instance **restores half of that
  instance's added minutes** to the pool it was lost from. Weekly earn-back is capped at
  that week's applied loss (redemption can recover, never mint surplus). Restores apply
  to the CURRENT week's remaining budget.

## D. NEW — Threshold pay + redemptive reps (WeeklyFrequency tasks)

- A `WeeklyFrequency` task (e.g. Walk Gemma 3×/week, $5 each) pays **all-or-nothing**:
  hit the full target → `EarnValue × target`; fall short by any amount → $0.
  Reps can happen on any days (Sun–Sat); there are no day slots for these chores.
- **Miss counting for ST:** shortfall is assessed at reconciliation
  (`misses = target − credited reps`), each shortfall unit priced as one missed instance
  under §A. (These chores cannot contribute to the mid-week accrual until the week can no
  longer reach target; at that moment their pending loss appears.)
- **Credit rules follow §3.3:** an Excused/parent-completed rep counts toward the
  threshold (a sick day must not nuke the $15).
- **Redemptive reps:** reps beyond the target earn screen-time back at **half the chore's
  per-instance price**, credited to **next week's** pool. Cap: the current week's applied
  loss (same no-minting rule as §C). Redemption stays available in a busted week — that
  is the point: after pay is forfeited there is still a live reason to keep going.
- Data: `ChoreDefinition.AllOrNothing` (bool, default true for WeeklyFrequency earning
  chores); redemption credits persisted per (chore, child, week).

## E. NEW — At-risk escalation rule (kid-facing)

The kid's "At Risk Today" card computes, per incomplete chore:
- Day-prescribed (`SpecificDays`): at risk **on its day** ("due tonight").
- WeeklyFrequency: **urgent when `days_left == reps_left`** (must do it every remaining
  day); **amber warning when `days_left == reps_left + 1`** ("gets tight Thursday").
- Card shows the exact stakes (money + minutes) and a day total ("on the line today:
  $15.00 + 27 min"). It renders ONLY true states — calm state says "nothing at risk
  today" with at most one preview line for the next pinch. Never nag all week.
- Same rule drives the Friday-style push notification.

## F. NEW — The Time Machine (gated retro-correction)

Past days become **viewable read-only** (they are currently unreachable); editing the past
is a request/adjudication flow:

- Kid opens a past day → rows dimmed, day marked PAST/locked. A `Missed` chore offers
  **"Time Machine"** → request = (chore, date, required free-text note).
- **Allowance:** 3 requests/week (configurable per child). **Window:** current week only;
  closes at Sunday reconciliation. After reconciliation only a parent-initiated
  adjustment can touch a settled week.
- Parent approval card shows the claim + an **exact effect preview** (rep counter change,
  threshold status, pending-ST delta) before Approve/Deny.
- Approve → mark the log `Approved` as of that date and **recompute** money (slices /
  thresholds) and pending ST for the week. Deny → unchanged. Both notify the kid.
- Implementation: mirror the existing Help-request machinery (`TrackerService`
  `RespondToHelpRequestAsync` pattern + `ChoreHub` push) with a new request type; do not
  build a parallel system.
- Every Time Machine approval and every manual slider adjustment lands in the ST ledger
  as a labeled entry ("Parent adjustment", "Time Machine: Walk Gemma Tue") — mercy on the
  record, never silent edits.

## G. Data-model delta (supersedes §6 where it conflicts)

```
ChoreDefinition:
  + int  Importance            // 0–10 (replaces ScreenTimeSubtractor)
  + bool AllOrNothing          // WeeklyFrequency threshold pay
  + string LucideIconName      // icon system (see design handoff README)
  + string Hue                 // violet|pink|mint|blue|amber
  (IsInverseFill / InverseFillBaselineMinutes stay, per §4.4)

ChildProfile:
  + decimal WeekdayScreenTimeHours, WeekendScreenTimeHours   // pool totals
  + int WeekdayAtRiskPercent = 30, WeekendAtRiskPercent = 50
  + int WeeklyFixRequestAllowance = 3
  (WeeklyRoutinePayout stays, per §3)

New: QolShare        (choreId, childId, sharePercent, isLocked)
New: ScreenTimeEntry (childId, week, pool, kind: Deduction|EarnBack|Adjustment|TimeMachine,
                      choreId?, minutes, streakMultiplier?, note)
New: FixRequest      (choreLogId, note, status: Pending|Approved|Denied, decidedBy, decidedAt)

WeeklyReconciliationService:
  shortfall counting for WeeklyFrequency → §A pricing → streak multiplier → clamp at pool
  budget → apply earn-back credits (capped) → write ScreenTimeEntry rows → set next week's
  budgets → recompute QOL soft targets from AppliedWeeklyLoss × shares → close Time Machine
  window → routine payout per §3.2 (unchanged).
```

---

## H. Implementation status (core backend done 2026-07-06)

Built, building clean (0 warnings), **106 tests passing**. Migration
`20260706070703_AddScreenTimeMechanics` (backfills Kind + child ST defaults) — **NOT yet
applied to any DB** (run `dotnet ef database update` when ready).

**Done this pass:** §A importance-share pricing, §B curve (simplified to **×1/1.5/2/3** frozen —
your confirmed choice), §C QOL shares + share-mixer rebalance, §D threshold pay + redemption,
plus renames, miss-counting-from-schedule, `ScreenTimeEntry` ledger, and a **hosted reconciler**
(wired + a real gating bug fixed — `IsReconciliationNeededAsync` now targets the just-completed week).

**Confirmed-decision deltas from the doc above:**
- Busted week (target missed) = **ST-only earn-back, no half-money** (all-or-nothing is a hard
  cash cliff). Money-or-ST choice applies only to over-target extra reps.
- Flex (WeeklyFrequency) reps price against the **weekday** pool.
- Pricing honors schedule Overrides (Add/Remove/Move).
- QOL soft target persists as an equal-split scalar (`InverseFillAddedMinutesPerRoutine`); the
  per-routine share-weighted target is a display-time compute from `QolShare × AppliedWeeklyLoss`.

**Service surface the UI binds to (all exist):**
- `IScreenTimePricingService.GetWeekPricingAsync(childProfileId, anyDateInWeek)` → per-instance
  **minute prices** for the live-price display (never leak the importance number).
- `IQolShareService` (GetShares/SetShare/SetLock; rebalance via pure `QolRebalancer.Rebalance`).
- `ChildProfileService.UpdateScreenTimeSettingsAsync(...)` (two ST sliders + payout + at-risk %).
- `TrackerService.SetRedemptionChoiceAsync(choreLogId, choice, userId)` (money-vs-ST node).
- `ChoreDefinitionDto`/`TrackerChoreItem` carry `Importance` + `AllOrNothing`.

**Deferred to the next backend pass (as agreed):** §E at-risk/accrual computation service; §F
FixRequest + Time Machine adjudication (`WeeklyFixRequestAllowance` column already added so no
schema churn later); and the **runtime QOL completion earn-back** (§C "completing a boosted QOL
instance restores ½ its added minutes to the *current* week") — reconciliation-time redemption
earn-back IS done; the completion-time hook is not.
