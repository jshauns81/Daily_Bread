# Native App — Improvement Backlog

> Working list for the Phase 2 polish loop. Ranked by impact ÷ effort.
> Research note: the market's failure modes are consistent — kids disengage
> within 2–3 weeks without a delight loop; parents abandon over approval
> friction and sync unreliability. Every "Now" item below attacks one of
> those two risks.

## Now — high impact, small effort

1. **App icon + launch identity.** The app still wears the default blank
   icon. The year-grid concept (heatmap as the mark, one cell gold) from the
   design board becomes the real AppIcon asset. Cheapest possible "feels
   real" win.
2. **The completion moment.** Checking off a chore is the core loop and
   currently it's just a toggle. Add: ring animates up, the earn value pops
   ("+$2.50") and flies toward the balance, success haptic. When the last
   chore completes: a celebration (the web app's confetti is a signature —
   port the moment, not the implementation).
3. **Batch approve.** "Approve all (N) — $12.50" button with one confirm.
   One-by-one approval of a real week is parent friction of exactly the kind
   that kills these apps.
4. **Tab badges.** Approvals tab shows the waiting count (`.badge`). Parents
   should see the number before they tap.
5. **Foreground auto-refresh.** Refresh stores on `scenePhase == .active`.
   Stale data reads as "the app is broken" — reliability is habit-formation.
6. **Pending-first sort.** Done chores sink to the bottom of Today; what's
   left to do is always at the top.
7. **Streak on the kid's Today.** "🔥 12-day streak" beside the greeting.
   Server already computes CurrentStreak (child dashboard) — needs a small
   API exposure. Streaks are the single most proven retention mechanic.
8. **Balance glance on Today.** The kid's gold balance in the Today header,
   tappable → Earnings. The money is the motivation; keep it one glance away.

## Next — high impact, medium effort

9. **Goals in the kid app.** API fully exists. Primary-goal card with
   progress on Earnings (and mini progress on Today). "Save toward the
   thing" is the financial-literacy heart of these apps.
10. **Parent drill-in.** Tap a kid tile on Home → that kid's day (the
    today endpoint already accepts userId). Toggle-for-them from there.
11. **Richer Help sheet.** Show the earn value and "raised 24 min ago"
    (needs earnValue added to the help-request DTO — tiny API change).
12. **Home-screen widget.** Kid: ring + first two open chores + balance.
    Parent: waiting-on-you count. Needs an app group + widget extension
    target. Biggest "alive outside the app" feature available before push
    notifications exist.
13. **Heatmap day detail.** Tap a cell → popover with that day's summary
    (the calendar API already returns everything needed).
14. **Weekly-frequency chores UI.** Weekly chores (3× per week etc.) render
    as progress bars in Today ("2 of 3 this week"). API ready
    (chores/week); UI missing.
15. **Earnings trend chart.** Swift Charts over ledger history — week/month
    earned, gold bars. Parents love the trend; kids love watching it grow.

## Later — bigger lifts or gated

16. **Push notifications (Phase 3).** The market's #1 parent complaint is
    approval lag. APNs lands after the dev license; this is the moment that
    unlocks it. Until then, foreground refresh (item 5) is the mitigation.
17. **Achievements.** Server endpoints are still deferred (Phase 1b);
    surface once they exist. Rank/badge progression is a proven kid hook.
18. **Cash-out flow in app.** Payout endpoints deferred server-side. The
    "Cash out ready" badge exists; the tap-through doesn't yet.
19. **Live Activity** (day progress on the lock screen), **App Intents**
    ("mark trash done" via Siri), watchOS glance.
20. **macOS polish.** ⌘⏎ approve / ⌘F forgive keyboard shortcuts, menu-bar
    commands, window min-size, multi-window.

## Robustness — trust is a feature

21. **Read cache.** Persist last-fetched Today/dashboard so launches are
    never blank offline; refresh replaces. (Writes stay online-only by
    design — money wants server truth.)
22. **Proactive token refresh** shortly before expiry instead of reacting
    to a 401 (removes a rare visible hiccup).
23. **Accessibility pass.** VoiceOver labels for rings and heatmap cells,
    Dynamic Type audit, confirm 44pt targets.
24. **Light-mode QA.** It works; nobody has *looked* at it seriously yet.

## Suggested sprint order

- **Sprint A (the feel):** 1, 2, 6, 8, 7 — the kid's experience becomes joyful.
- **Sprint B (the parent):** 3, 4, 5, 10, 11 — approval friction eliminated.
- **Sprint C (the hook):** 9, 12, 13, 14 — reasons to come back daily.
- Then reassess against Phase 3 (license + push).
