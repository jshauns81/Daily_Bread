# Household isolation audit — 2026-08-04

Trigger: Shaun's "happy accident" — a parent account outside every household
(created that evening, see root cause) saw Victor's balance, earnings, and
help queue on the native Home/Approvals screens while guarded screens were
correctly empty. Two read-only audit passes swept every API controller and
the service layer the same night.

**Structural finding:** only `ApplicationUser` and `ThemeFile` carry a
`HouseholdId`. The service layer is household-blind; isolation exists only
where a controller adds it. Every fix below therefore either adds a guard at
the controller or threads a household filter into the specific service call.

## Fixed (commit-verified, tests in HouseholdGuardTests + DashboardHouseholdScopeTests)

1. **Dashboard read leak** — `GET /dashboard/parent` returned every family's
   balances, week earnings, child names, progress, pending approvals, and
   help texts. `GetParentDashboardAsync` now takes a household and filters
   every constituent query; the API passes the caller's household and returns
   an empty dashboard for the household-less. Unscoped remains only for the
   single-family Blazor pages.
2. **Approvals read leak** — `GET /approvals` (same service call): scoped the
   same way; family-less callers get an empty queue.
3. **Cross-household chore toggle (write)** — `POST /chores/{id}/toggle`
   guarded the target *user* but not the chore: any caller could complete or
   approve (minting earnings) another family's chores. New guard
   `ChoreDefinitionIsAssignedToAsync`: the chore must be assigned to the
   resolved target.
4. **Cross-household help injection (write)** — `POST /chores/{id}/help` had
   no guard at all; attacker-controlled text was push-notified to the victim
   household's parents. Now "self only" is enforced: the chore must be
   assigned to the caller.
5. **Planner fail-open visibility** — chores with an unassigned or
   null-household assignee were visible, editable, re-assignable, and
   deletable across families (`GET/PUT/DELETE /planner/chores*`). The
   visibility check now fails closed like the guard.
6. **Users born household-less (root cause)** — `CreateUserAsync` never set
   `HouseholdId`; every post-migration account floated outside all guards and
   inside every fail-open path. New members now take the requested household,
   defaulting to the deployment's single active household when exactly one
   exists.
7. **Achievement definitions CRUD** — Parent-role callers from any household
   (or none) could enumerate and rewrite every family's badge definitions and
   reward amounts. Interim gate: caller must belong to a household (list is
   empty, mutations 403, otherwise).
8. **Family features** — the single settings row was readable/writable by
   household-less accounts; gated to household members. Also fixed in
   passing: `EnableGoals` was dropped by `UpdateSettingsAsync` — the goals
   toggle returned 200 OK and changed nothing.

## Deferred (schema or design work — tracked, not forgotten)

- **`HouseholdId` on `Achievement`, `FamilySettings`, `ChoreDefinition`** —
  the real multi-tenant fix for items 5/7/8: per-family badge definitions,
  per-family week-start/cash-out settings (today one row drives every
  family's money math), and owned-but-unassigned chores. Needs migrations +
  seeding decisions (per-household default badge sets).
- **Push + SignalR fan-out** — `SendToRoleAsync("Parent")` and the single
  `ParentsGroup` hub group broadcast help/chore events (child name, chore,
  help text) to every parent account in the DB. Correct with one family;
  needs per-household groups/subscriptions before a second family exists.
  Same for the single ntfy topic (deployment-wide by configuration).
- **Blazor-only global paths** — `CanAccessProfileAsync` /
  `CanAccessAccountAsync` short-circuit to "any profile" for parents;
  `GetAllUsersAsync`, global tracker/planner/chart queries. Not reachable via
  the API today; booby traps for any future endpoint that trusts them.
- **Weekly reconciliation runs globally** (background job, no API route) —
  acceptable as a job, but one global week-start drives every family's math
  until FamilySettings is per-household.
- **`GET /chores/today` hand-rolls its guard** — semantically equivalent to
  `ResolveTargetUserAsync` today; should be refactored onto the guard so the
  two can't drift.

## Non-security notes from the sweep

- Ledger balance sums account-scoped rows while history filters by user —
  an account-written row yields balance-without-history.
- Driving pending-approvals path is fail-closed but N+1 and silently skips
  household members without the Child role.
