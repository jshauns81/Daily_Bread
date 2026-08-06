# Backlog — capabilities noticed missing, written down before they fade

Entries are dated and stay until shipped. The rule: capture the finding,
what already exists, and what's actually left — so a fresh session can
build without re-deriving.

## Parent check-off — complete a chore on a child's behalf (2026-08-05)

**Shaun's report:** Blazor let a parent mark a child's chore complete —
not excuse it, *complete* it, shown in the front end as though the child
did it. The native app can't. For the family this is core workflow: the
ADHD co-checkoff — parent and Victor walking the list together, checking
off as they go — is one of the reasons the app exists.

**What already exists (verified 2026-08-05, nothing to build here):**

- API: `POST /api/v1/chores/{id}/toggle` already accepts `body.userId` —
  "children toggle their own; parents may toggle for a child in their
  household." `ResolveTargetUserAsync` + `ChoreDefinitionIsAssignedToAsync`
  (the 2026-08-04 audit guards) enforce the household boundary and that
  the chore belongs to the target child. The `isParent` flag already flows
  into `ToggleChoreCompletionAsync`.
- Schema: `ChoreLog.CompletedByUserId` records who actually flipped it —
  the Blazor era stamped the parent's id here while displaying the child
  as completer. The data model already tells the truth.
- Workaround today: the Blazor Tracker in production still has the
  capability; parents can use the web app until the native surface ships.

**What's actually missing — native UI only:**

- A parent-side surface listing a chosen child's day with tappable
  completion, calling the existing toggle endpoint with the child's
  userId. Natural homes: the child's detail screen, or a "run the list"
  mode on the parent planner.
- Display decision to make at build time: Blazor showed surrogate
  completions as the child's own. Since `CompletedByUserId` already
  distinguishes, the native UI could subtly mark "checked by Dad" — or
  deliberately not, matching Blazor. Shaun's call when it's built.
- Confirm `isParent` toggle semantics (auto-approve vs pending) match the
  expected flow before shipping the button.

No schema work, no new endpoints, no security surface — the audit's
guards already cover exactly this path.
