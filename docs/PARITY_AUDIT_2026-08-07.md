# Blazor ↔ iOS parity audit — 2026-08-07

Every Blazor page read against every iOS feature view, the night the family
asked "have we gotten it all?" Short answer: the daily loop has full parity
and iOS is ahead in a dozen places — but the **money lifecycle ends at a
balance number** on iOS, and a handful of parent powers never crossed over.

## The headline gaps (family-facing, in build order)

1. **Cash-out does not exist on iOS at all.** Blazor has two flows (parent
   per-account via `Ledger.razor` → `ProcessAccountCashOutAsync`; child
   self-serve via `MyBalance.razor` → `ProcessCashOutAsync`), both through
   `CashOutModal` with 25/50/75/All quick-fill and a threshold/resulting-
   balance preview. iOS's "Cash out ready" capsule on parent Home is
   display-only and leads nowhere. No cash-out route exists in
   `api/v1/ledger` yet (the controller has adjust/balance/history) — small
   server addition + client method + sheet. **Folds into the ledger build.**

2. **Parent ledger view.** `client.history(userId:limit:)` already accepts a
   userId; nothing calls it with one. A parent cannot see a child's ins/outs
   on iOS. The Blazor stats tiles (earned/deducted/bonuses/penalties/paid
   out) come from `GetAccountTransactionStatsAsync`, which has no iOS wire
   type yet.

3. **Complete-as-child is one wiring away.** `TodayView(userId:)` +
   `ChoreRow(isParentActing:)` already support a parent acting on a child's
   row — but nothing ever constructs `TodayView` with a userId
   (`MainView.swift` passes none; parent Home drills into `ActivityView`
   instead). Blazor parents can also complete on ANY date
   (`IsCheckboxDisabled` returns false for parents); iOS `ActivityView` can
   backdate approve/miss/excuse/reset but not a completion — Pending rows
   only offer "Excuse".

4. **Bonus/penalty typing lost.** iOS's `AdjustBalanceSheet` posts
   everything as Adjustment; Blazor records Bonus and Penalty as distinct
   types the stats report on. Preserve the types when the ledger lands.

## The one correctness risk (not a gap — a divergence)

**The planner grid's tap means different things on the two platforms.**
Blazor's grid cell = one-off per-DATE override (`ToggleOverrideAsync`),
with week navigation. iOS's grid cell edits the chore's RECURRING
`activeDays` — same gesture, permanent effect, no week concept, no
override list. A parent "moving Tuesday's chore" on iOS is rescheduling
every future Tuesday. Decide deliberately: bring overrides + week nav to
iOS, or make the iOS grid's recurring-rule semantics visually unmistakable.

## Smaller family-facing gaps

- **Goals:** can't "Mark as Achieved" on iOS (`CompleteGoalAsync` unwired);
  no goal image URL field.
- **Driving:** can't edit the hours goals (`UpdateDrivingGoalAsync`), can't
  delete an entry, no date-range filter on history/CSV export.
- **My Balance extras (kid):** progress-to-cash-out bar, lifetime
  earned/deducted/cashed-out tiles.
- **Achievements:** no category filter, no detail view, no unlock
  celebration (marked seen silently), and the Active Bonuses panel
  (multipliers, streak protection, …) has no endpoint at all.
- **Settings triad:** cash-out threshold, family time zone, child
  self-report toggle — all Blazor-only.
- **Change password (self-service)** — missing on iOS.
- **Activity:** no "All Children" grouped view; no balance in header.
- **Calendar:** no month stats/streaks, no month/year jump.
- **Printable weekly chore chart** (`/chore-chart`) — no iOS equivalent.
- **Admin:** create user / change role / delete user are deliberately
  web-only (FamilyMembersView says so); reset-password and lock/unlock made
  it across.
- **Platform plumbing:** push notifications (R5 covers it), SignalR live
  updates (iOS polls), OIDC sign-in, explicit light/dark override.

## Where iOS is ahead (no web equivalent)

Server pairing, the biometric parent gate + privacy cover + protected
session, custom YAML themes with sync and contrast advisories, widgets +
macOS dock icon, approve-all batch, reject-with-reason (rewards & driving),
per-child driving visibility, family feature flags, screen-time history
sheet + at-risk card + consequences dial, member rename, kid's own
reward-claim status, 14-day earnings chart, celebration tiers, planner
grid/list toggle + drag-to-paint, macOS sidebar with ⌘1–⌘6, native CSV
export, and a settled-week caveat banner on backdated Activity actions
that Blazor doesn't have.

Full per-page capability map lives in the audit transcript; this file keeps
the actionable deltas. Cross-references: `docs/BACKLOG.md` (parent
check-off spec), `MECHANICS_AMENDMENT.md` §F (Time Machine),
`CHORE_SCREENTIME_REDESIGN.md` (routine payout — already live end-to-end,
editable in iOS Settings → Screen Time & Pay).
