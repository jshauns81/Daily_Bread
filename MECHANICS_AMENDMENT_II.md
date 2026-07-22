# MECHANICS AMENDMENT II — Bounded minute prices

> **Supersedes** MECHANICS_AMENDMENT.md **§A** (importance shares), **§B** (compounding
> multiplier in penalties), and the penalty-calculation portions of **§C–§E, §G**. Where this
> document conflicts with any earlier one, **this document wins**. Money and routine-payout
> mechanics (CHORE_SCREENTIME_REDESIGN.md §3) are **unchanged**. Adopted 2026-07-20.

## Why

Schedule-normalized importance shares concentrated the whole pool budget onto a single chore when
a pool held few chores: one weekend chore = 100% of the weekend at-risk budget, so a single missed
Saturday chore could cost the entire weekend. This amendment replaces shares with **bounded, stable
per-occurrence minute prices**, keeps the pool cap as the aggregate ceiling, and adds a forgiving,
proportional **late-repair** credit. Consequence that stings without turning one missed chore into a
lost weekend.

## Rules (authoritative)

1. **Per-occurrence price = Importance × 6 minutes.** Importance 0–10 maps directly to 0–60 minutes.
   Importance 0 = no screen-time impact.
2. **Schedule normalization is removed.** A chore's price never depends on how many other chores exist.
3. **The consecutive-week streak multiplier is removed from penalty calculations.** (It may remain for
   other features; it no longer touches screen-time penalties.)
4. **Missed-occurrence prices are summed within each pool** (weekday, weekend) independently.
5. **Total weekday loss is capped** at the child's configured `WeekdayAtRiskPercent` of the weekday
   pool (default 30%).
6. **Total weekend loss is capped** at the child's configured `WeekendAtRiskPercent` of the weekend
   pool (**default changed to 20%**). The field stays configurable; only the default changes.
7. **Repair and redemption credits apply AFTER the pool cap.**
8. **Late completion restores 50% of that occurrence's proportionate applied share** (see Calculation).
9. **Late completion restores screen time only.** It does NOT restore missed money or routine payout.
10. **Help / excuse (parent mercy) gives FULL protection:** the occurrence is removed from the miss set.
11. **Time Machine (record correction) gives FULL correction** when the chore was actually done on time:
    the occurrence is removed from the miss set.
12. **All correction and repair paths close at weekly reconciliation.** After reconciliation, only a
    parent adjustment can alter a settled week.

## Occurrence price

```
price(occurrence) = clamp(Importance × 6, 0, 60)   // minutes
```

Because Importance ≤ 10, the 60-minute clamp is a **guardrail, not an active limit** — the scale *is*
the cap. Help/excused occurrences (rule 10) and on-time Time-Machine-corrected occurrences (rule 11)
are excluded from the miss set entirely, so they never enter `rawLoss`.

## Per-pool calculation order (authoritative)

For **each pool independently**, at reconciliation:

```
rawLoss       = Σ price(o)  for every MISSED occurrence o in the pool
poolCap       = round(poolHours × atRiskPercent / 100 × 60)
appliedLoss   = min(rawLoss, poolCap)
repairedValue = Σ price(o)  for every missed occurrence COMPLETED LATE (before reconciliation)
repairCredit  = (rawLoss == 0) ? 0 : appliedLoss × 0.5 × (repairedValue / rawLoss)
finalLoss     = appliedLoss − repairCredit
```

**Round to whole minutes once, at the very end** (after `finalLoss`). `finalLoss` is the number of
minutes removed from that pool for the week.

Properties:
- **Uncapped week** (`rawLoss ≤ poolCap`): `appliedLoss = rawLoss`, so
  `repairCredit = 0.5 × repairedValue` — exact half-credit of what was repaired.
- **Capped week** (`rawLoss > poolCap`): each repaired occurrence restores half of its *proportionate
  applied share*. Repairing everything reduces the capped loss by **exactly half — never to zero**.
  Every repair contributes; there is no dead zone.

Worked examples (weekend pool, 20h, 20% at-risk → `poolCap` = 240 min):

| Scenario | rawLoss | appliedLoss | repairedValue | repairCredit | finalLoss |
|---|---|---|---|---|---|
| Miss two importance-5 chores (30 each), repair none | 60 | 60 | 0 | 0 | **60** |
| Same two, repair one | 60 | 60 | 30 | 60·0.5·(30/60)=15 | **45** |
| Blown week: miss ten importance-10 (60 each), repair none | 600 | 240 | 0 | 0 | **240** |
| Blown week, repair all ten | 600 | 240 | 600 | 240·0.5·(600/600)=120 | **120** |
| Blown week, repair five | 600 | 240 | 300 | 240·0.5·(300/600)=60 | **180** |

## The three doors (distinct by intent)

| Door | Meaning | Restoration |
|---|---|---|
| **Help / excuse** | Parent mercy | **Full** — occurrence excluded from the miss set |
| **Time Machine** | Record correction (it was done on time) | **Full** — occurrence excluded from the miss set |
| **Late repair** | Kid completed it late, on their own | **Half** — proportionate applied share (rules 7–8) |

All three close at reconciliation.

## Weekly-frequency (flex) chores

Flex shortfalls price into the **weekday pool**. `shortfall = target − credited reps`; each shortfall
unit is one missed occurrence at `price = Importance × 6`. Threshold (all-or-nothing) money is separate
and governed by the §3/§D money rules; late repair never restores money (rule 9).

## Display

- The meter's **"on the line this week"** for a pool = `min(Σ price(schedulable occurrences in pool),
  poolCap)` — the real capped exposure, not the raw pool budget.
- Per-chore **"worth"** = `price = Importance × 6`, shown **neutrally** (not as a red loss).
- **Red remains reserved for the Help alert.** The cost list is not red.

## What this supersedes

- **§A** importance shares → replaced by rules 1–2, 4.
- **§B** compounding multiplier → removed from penalties (rule 3).
- **§C/§D** redemption earn-back → unified into the proportional repair-credit formula (rules 7–8);
  the money-exclusion (rule 9) is retained.
- **§E** at-risk pricing → per-occurrence price is now `Importance × 6` (bounded); the at-risk card
  and mid-week accrual use these prices.
- **§G** deltas that assumed shares → the `AtRiskPercent` fields remain; `WeekendAtRiskPercent`
  default becomes 20.
