# Daily Bread — Native App Plan (SwiftUI · iOS / iPadOS / macOS)

> **DIRECTION UPDATE (2026-07-20, from Shaun — supersedes anything softer below):**
> The native app is the COMPLETE replacement, not a companion. The Mac app experience
> must be 100% — "not a hodge-podge." The Blazor web app is being **phased out** for
> Shaun's family and for any family on iOS/macOS; it survives only until native parity
> is reached, then goes to bed. **The planner is the cornerstone of the parent piece**
> and ships in the native app (iOS + macOS) as a first-class feature. Anything below
> that says "web remains the admin surface" is obsolete — admin/planner/management all
> go native.


> Status: **Proposed v1** · Created 2026-07-19
> Executes the decided direction: native SwiftUI app, backend stays, Blazor web shrinks to an admin surface. PWA/Capacitor and "should we migrate" are closed questions and not revisited here.

---


## Victor's experience — the KID side is a first-class parity target (Shaun, 2026-07-20)

"I don't want to forget a key piece which is my son's experience." This ties directly to the
earliest feedback ("the old app felt ALIVE, the new one doesn't"). The Blazor KID surface (Home,
Activity, Calendar, Balance, Achievements, Goals, Appearance) is gamified, and that gamification is
what makes it feel alive to Victor. Native kid tabs today = Today / Earnings / Awards / Settings —
functional but under-gamified.

KEY FINDING: the whole leveling system is CLIENT-SIDE presentation (Home.razor), derived from data
the native app ALREADY fetches — no backend work needed:
- Today level = % of today's chores done: 100→5 LEGENDARY 👑, ≥75→4 EPIC 💎, ≥50→3 RARE 🔥,
  ≥25→2 UNCOMMON ⚡, >0→1 COMMON ⭐, 0→STARTER 🌱 (rarity colours match the achievement rarities).
- XP = completed / total today. "N quests remaining" = pending chores. Badges = achievements
  unlocked (34/49). "Rest Day — No Quests" when nothing is scheduled.

Kid parity gaps (native status):
- **Gamified Home hero** — MISSING and highest-value. A native Today/Home hero that reframes today's
  progress as the LVL badge + XP bar + quests-remaining + badge count + latest achievement + streak.
  Pure native UI over existing data — THE "feels alive" win. Do this first on the kid side.
- **Calendar (kid's own)** — same monthly grid as the parent Calendar, scoped to self.
- **Balance** — native Earnings is close; align to the Blazor Balance: big balance, progress-to-cash-out
  bar, Total Earned / Deducted / Cashed Out tiles, recent transactions (all from existing ledger APIs).
- **Achievements celebration** — the "🎉 New Achievements Unlocked!" moment + category tabs + Bonuses.
  Native Awards shows them but not the unlock celebration; wire the new-achievement moment.
- **Goals** — native goal exists but gated (enableGoals); Blazor kid has a full Goals tab. Elevate.
- **Appearance** — DONE (native theme picker already matches, including the gold/red invariant copy).

Kid build order: gamified Home hero → Balance alignment → kid Calendar → achievement celebration →
Goals tab. All are native-UI tasks over existing endpoints; none need new backend. This runs in
parallel with the parent parity list — the two together are what "100% Mac" means.


## Blazor → native parity checklist (the "100% Mac" list, from Shaun's 2026-07-20 walkthrough)

The native app must reach full parity with the Blazor **parent** surface before Blazor is retired.
Shaun walked every parent screen. Status as of 2026-07-20:

Parent surface (Blazor sidebar) → native status:
- **Home** — DONE (ParentHomeView).
- **Activity** — NOT NATIVE. Blazor: per-day chore table for a child (Done/Pending/Excused rows,
  Undo/Excuse/Restore actions), date navigator, All-Children filter, day stat tiles
  (Total/Completed/Pending/Earnings today). Native has parent drill-in Today + Approvals but not
  the explicit Excuse/Restore day-table. Needs: excuse/restore endpoints + a native Activity screen.
- **Calendar** — NOT NATIVE (read API EXISTS: /api/v1/calendar/range powers the heatmap). Blazor:
  monthly grid, per-day completed/earned, colour states (All/Partial/None/NoChores/Future), month
  nav, stat tiles (Perfect Days / Completed / Missed / Earned / Current & Best streak), All-Children.
  Lowest-effort parity win — mostly a native monthly-grid view over the existing endpoint.
- **Planner** — PARTIAL. Native has the list + reorder + full chore editor (now Blazor-matched).
  MISSING: the Blazor **Tasks weekly grid** (7-day cells, click a cell to toggle schedule =
  per-date overrides: Scheduled/Available/Override/Removed), the Weekly-Potential card, and the
  Tasks/Routines split view. Needs: per-date override endpoints (ToggleOverrideAsync/ToggleDayScheduleAsync
  already exist server-side) + a native weekly-grid UI.
- **History** — NOT NATIVE (read API EXISTS: /api/v1/ledger/balance + /history). Blazor: balance
  summary (Current/Total Earned/Deducted/Paid Out + "need $X to cash out"), stat tiles
  (Earnings/Deductions/Bonuses/Penance/Paid Out/Transactions), transaction table (Payout/Task/…
  typed rows), and **+ Add Adjustment / + Bonus / − Penalty**. Read side is ready; needs an
  adjustment endpoint + a native History screen.
- **Achievements (management)** — NOT NATIVE. Blazor: CRUD grid — add/edit/deactivate, rarity
  (Common…Legendary), points, category, unlock conditions (StreakDays/PerfectDays/TotalEarned/…),
  tangible rewards. Kids already SEE achievements natively (AchievementsView); parents can't MANAGE
  them. Biggest piece — needs a full achievement-management API + native editor.
- **Reward Claims** — NOT NATIVE. Approve/deny kids' reward redemptions (cash/tangible). Overlaps
  the existing AchievementRewardClaim backend; needs claim list + approve/deny endpoints + UI.
- **Users** — NOT NATIVE. Add/manage children + parents, passwords, roles. Ties into the onboarding
  auth plan (§5d). Sensitive; do last with care.

Suggested build order (cheapest-parity-first, each its own sprint):
1. **Calendar** (read API exists) → 2. **History** read + adjustments → 3. **Activity** (excuse/restore)
→ 4. **Planner weekly grid** (per-date overrides) → 5. **Achievements management** →
6. **Reward Claims** → 7. **Users/admin**. Then the Time Machine (§F) is the one thing that was
never on the web. Only after all of this is Blazor retired for Apple families.

Design bar (from real-device review): sheets/editors are laid out with the shared **SheetKit**
(Components/SheetKit.swift) — explicit label-above-control cards, NOT macOS `Form` (which renders
cramped). The chore editor matches the Blazor edit dialog (icon+name, Earns/Expected, schedule
rule, day circles, grouped Options with date range + notes). Every native screen must look at least
as finished as its Blazor counterpart — "complete, not a hodge-podge."

## 0. Summary

Think of the backend as your switch fabric: Postgres + EF Core + `Services/` is the core, and the Blazor web UI is currently the only access port patched into it. This plan **adds a second access port — a versioned HTTP API — in front of the same services**, then builds one SwiftUI codebase that runs natively on iPhone, iPad, and Mac against that port. Nothing gets re-cabled. The web app keeps working the whole time and only shrinks to admin duty at the end, after the family has actually switched.

```
                    ┌─────────────────────────────────────────────┐
                    │  dailybread-app container (unchanged deploy) │
 iPhone / iPad ──┐  │  ┌───────────────┐   ┌───────────────────┐  │
 (SwiftUI)       ├──┼──►  /api/v1/*    ├───►                   │  │   ┌──────────────┐
 Mac (SwiftUI) ──┘  │  │  JWT bearer   │   │   Services/       ├──┼───►  Postgres     │
                    │  └───────────────┘   │   (existing,      │  │   │  (unchanged)  │
 Browser (admin) ───┼──►  Blazor Server ───►   untouched)      │  │   └──────────────┘
                    │      cookie auth     └───────────────────┘  │
                    └─────────────────────────────────────────────┘
                              ▲ Cloudflare tunnel (already in place)
```

**The five headline decisions (details + reasoning in §1):**

| # | Decision | Call |
|---|----------|------|
| D1 | Database | **PostgreSQL stays, server-side, as-is.** The app never talks to the DB. |
| D2 | Native auth | **Token endpoints (JWT access + rotating refresh, Keychain).** Cookies stay for web. |
| D3 | Hosting | **Stay fully dockerized.** API lives in the same process/container. |
| D4 | Distribution | **Apple Developer Program ($99/yr) + TestFlight** internal group for the family. |
| D5 | Real-time | **APNs push is the native reliable channel**; ntfy stays as fallback; SignalR stays for web. |

---

## 1. Decisions and why

### D1 — PostgreSQL stays. Do not convert it.

The instinct to ask "or convert it into something else" is worth answering head-on:

- **The DB is multi-user server truth** — money, approvals, penalties. Three users on four+ devices need one arbiter. That's a server database's job, and Postgres is already doing it well.
- **CloudKit / SwiftData-as-primary** would mean porting every rule in `Services/` (penalty timing, replay-safe approvals, reconciliation) into Swift and solving multi-user sync with roles — months of work to land somewhere worse for auditability. SwiftData's role in this plan is **on-device read cache only, and only if ever needed** (Phase 5, optional).
- **SQLite server-side** would gain nothing; you'd lose what EF Core + migrations already give you.

Networking analogy: the app is a new client VLAN. You don't move the core router into the client because a new VLAN showed up.

### D2 — Token auth for native clients; cookies untouched for web

The AUTH_MIGRATION_PLAN's "one cookie" model stays exactly right **for the browser**. Native clients get a parallel scheme against the same ASP.NET Identity store:

- `POST /api/v1/auth/login` (username/password → **the kid's path**, and parent break-glass) returns a short-lived **access JWT (~15 min)** + a **rotating refresh token (~90 days, revocable, stored server-side hashed)**.
- **Parents/SSO:** `ASWebAuthenticationSession` runs the Authentik auth-code+PKCE flow in-app; the backend validates the Authentik token at `POST /api/v1/auth/sso-exchange`, maps groups → roles exactly as the web callback does, and issues the *same app tokens*. One identity store, two front doors — same shape as the cookie-alignment decision, just token-flavored.
- Tokens live in the **Keychain** (shared app group, so future widgets can read them).

Why not just reuse the cookie from the app? URLSession can carry cookies, but background refresh, APNs-triggered fetches, widget timelines, and macOS all want a credential you can attach explicitly per-request and refresh without a WebView. Cookies in native apps are the RJ45-in-an-SFP-cage move — it can be made to fit, but you'll fight it forever.

JWT bearer is added as an **additional** authentication scheme in `Program.cs`; `[Authorize]` policies and roles work identically for both schemes. Zero change to web auth.

### D3 — Backend stays fully dockerized

The API is new endpoints **in the same ASP.NET process**, so nothing about the deploy story changes: same image, same `deploy.sh`, same Cloudflare tunnel, same DataProtection volume. DB-only containerization would buy marginally easier attach-a-debugger on the host in exchange for owning service management, .NET runtime updates, and drift on the host. Not worth it. Revisit only if you leave Docker generally.

One nice side effect: the app is already public at `https://dailybread.example.com`, so the native app works on cellular from day one with no VPN/Tailscale dependency.

### D4 — Apple Developer Program + TestFlight (deferred until the prototype works)

**Decision (2026-07-19): enroll only after a working prototype.** Sequencing:

- **Phases 0–2 run on the free personal team:** simulator for development, direct-cable installs to real iPhones for testing. Free-team installs **expire after 7 days** (re-plug + rebuild weekly) and there's **no push entitlement** — acceptable for prototyping, a dead end for daily-driver use.
- **The $99/yr enrollment is the gate between Phase 2 and Phase 3:** it unlocks APNs (Phase 3's whole point) and TestFlight (internal group: the maintainer, the co-parent, the kid — builds last 90 days, auto-update), plus App Store distribution later if ever wanted. Budget ~1–2 days for enrollment processing when the time comes.
- Interim: the Phase 2 prototype leans on ntfy (already reliable) for any notification needs during testing.
- Note: App Store Connect uploads (TestFlight included) have required the iOS 26 SDK since April 2026 — a non-issue since you'll build with current Xcode, but it pins the toolchain floor.

### D5 — APNs replaces web-push as the reliable native channel

Current stack: ntfy (reliable), web push (best-effort), SignalR (in-app). For the native app:

- **APNs** becomes the first-class channel — Help alerts, approval nudges, "chores due" reminders — sent from a new `ApnsPushService` beside the existing `PushNotificationService` (device tokens stored like `PushSubscription` rows; APNs auth via a `.p8` key in env). Alert-priority APNs is as reliable as it gets on Apple hardware.
- **ntfy stays** as the parent fallback channel — it works today; don't remove a working alarm circuit while rewiring.
- **In-app live updates:** an official **Swift SignalR client from Microsoft now exists** (`dotnet/signalr-client-swift`), so the app *can* join the same hubs the web uses. But don't make it a dependency for v1: APNs + refetch-on-foreground covers the family's actual latency needs. Wire SignalR in Phase 4 if the Today screen feels stale in practice.

---

## 2. The phased plan

Each phase is independently shippable and leaves the web app fully functional. Verification gates are on real devices, per house rules.

### Phase 0 — Backend groundwork (invisible to users)

> **Status: implemented 2026-07-19** (in working tree, pending commit). Delivered: `Api/` layer (DTOs with money-as-string + DateOnly conventions, `ApiTokenService` with JWT access tokens + hashed rotating refresh tokens with reuse detection, `ApiCurrentUserContext`), `AuthController` (login/refresh/logout/me — `CheckPasswordSignInAsync`, no cookies), `ChoresController` (`GET /api/v1/chores/today` with household isolation), JWT bearer as an additional scheme, `RefreshTokens` table + `AddRefreshTokens` migration, `/api/v1/health`, OpenAPI in dev, 11 new tests (70/70 suite green). **Before production deploy: set `JWT_SIGNING_KEY` env in compose** (`openssl rand -base64 48`) — without it the app warns and uses an ephemeral key (tokens die on restart).

1. **DTO layer** (`Api/Dtos/`): never serialize EF entities. Wire conventions decided once:
   - camelCase JSON; dates/times ISO-8601; **`DateOnly` as `"yyyy-MM-dd"`**.
   - **Money as decimal-serialized-to-string** (`"12.50"`) → decode straight into Swift `Decimal`. Never let cents ride through a JSON double.
2. **`/api/v1` skeleton** — controllers grouped to mirror `Services/` (ChoresController → `ChoreLogService`, LedgerController → `LedgerService`, …). Controllers stay thin: validate → call service → map DTO. Business logic does not migrate into controllers.
3. **Auth endpoints**: login, refresh (rotation + reuse detection), logout/revoke, sso-exchange. JWT bearer scheme added alongside cookies. Claims-based implementation of the existing `ICurrentUserContext` so services don't know or care which door the user came through.
4. **OpenAPI** via `Microsoft.AspNetCore.OpenApi` — this document becomes the contract the Swift models are generated/written from.
5. **API integration tests** in `Daily_Bread.Tests` (login → token → call → assert), so the contract is pinned before any Swift exists.

**Gate:** `curl` a full token → today's-chores round trip against staging; web app byte-for-byte unaffected.

### Phase 1 — The API surface that matters

> **Status: implemented 2026-07-19** (commit `c755e5e`). Chore toggle + Help raise + weekly progress; approvals queue with quick-approve and help-respond; ledger balance/history; goals CRUD; calendar range (heatmap source); parent dashboard. `HouseholdGuard` centralizes isolation (children→self only, parents→own household, chore-log ids guarded); suite 79/79. Deferred to a later pass: achievements endpoints, driving log endpoints, payout/cash-out actions.

Read endpoints: today's chores (per child), dashboard summary, balance, week progress, **year heatmap data**, achievements + progress, savings goals, ledger history (paged), driving log.
Write endpoints: complete/uncomplete chore, **raise Help**, approve/forgive (parent), ledger payout, goal CRUD, driving log entry + approval, reward claim + approval.

Sharp edge to settle here, permanently: **"today" is defined by the server** (family timezone in `FamilySettings`), and the client always passes the date it's asking about explicitly. Day-boundary bugs in a chore app are the equivalent of NTP drift in a logging stack — settle the clock authority once.

**Gate:** every endpoint exercised by integration tests incl. the auto-penalty/Help protection path.

### Phase 2 — SwiftUI MVP, child-first (first TestFlight)

> **Status: starter implemented 2026-07-19** (`apps/DailyBread/`, commit `4798285`). DailyBreadKit (wire types, APIClient with silent refresh, Keychain SessionStore, Graphite & Glass design system) + app shell (server onboarding, login, role-based tabs/sidebar, optimistic Today with Help flow, Earnings, parent Home + Approvals with the gold moment, Settings with accent themes). Builds clean for macOS and iOS Simulator; kit tests 11/11. **Remaining for the Phase 2 gate:** run it in Xcode with signing (Personal Team), point at the dev server, then the real-device pass on the kid's iPhone.

New repo (or `apps/` folder): **multiplatform SwiftUI app** (one target: iOS + iPadOS + macOS — native, not Catalyst) + a **`DailyBreadKit` Swift package** holding everything shareable:

```
DailyBreadKit/            (SPM package — models, no UI)
  Models/                 DTO mirrors (Codable, Decimal-safe money)
  Networking/             APIClient (actor, async/await, auto-refresh on 401)
  Auth/                   Keychain store, token lifecycle, ASWebAuthenticationSession flow
  DesignSystem/           Colors, spacing, type, haptics, motion tokens (§4)
DailyBread/               (app target)
  Features/Today/  Balance/  Login/  …    (one folder per feature)
```

- Swift 6, `@Observable` stores per domain (TodayStore, BalanceStore…), no third-party dependencies to start.
- **Optimistic UI** for chore completion (tick locally, reconcile with server response, roll back on failure) — this is where native will instantly feel better than Blazor-over-SignalR.
- Screens: Login (password form — the kid first), **Today** (chores, complete, raise Help), Balance, minimal settings.
- **Offline policy for v1: read-cache yes, queued writes no.** Money and approvals want server truth; a chore marked done offline that silently syncs after the penalty window is a correctness bug, not a feature.

**Gate:** the kid's actual iPhone via TestFlight: complete a chore → appears in web Activity; raise Help → penalty protection holds; kill app, reopen next day → still signed in (refresh token).

### Phase 3 — Parents, approvals, push

- **APNs plumbing** (backend `ApnsPushService` + device-token registration endpoint): Help alert → both parents' phones, tap → deep-link straight to the approval sheet.
- Parent screens: dashboard (family at-a-glance), approvals queue (chores, reward claims, driving log), the **Approve moment** with the gold-glow treatment + success haptic — invariant colors honored (§4).
- **Authentik SSO in-app** via `ASWebAuthenticationSession`. Break-glass password login remains, same as web policy.

**Gate:** end-to-end on real hardware: the kid raises Help → your phone buzzes → approve from the lock screen deep-link → his phone reflects it. That round trip is the product.

### Phase 4 — Daily-driver parity (the family switches)

- Ledger/History, cash-out/payouts, savings goals, achievements detail + claiming, driving log with CSV export via native share sheet.
- **Year heatmap as a first-class native piece** — custom `Canvas` grid, scrub with finger + per-cell haptic ticks, tap for day detail. This is the app's emotional high point; it should be *better* than the web version, not a port.
- iPad + **macOS polish**: `NavigationSplitView` sidebar, keyboard shortcuts, menu-bar commands (macOS is nearly free from the same codebase, but "nearly" = a real polish pass, not zero).
- Wage board / planner: **read view native; complex planning/editing stays web** for now — that grid is dense parent-admin tooling, exactly what the admin surface is for.
- Optional: SignalR Swift client for live Today updates if foreground-refresh feels stale.

**Gate:** one full real week — chores, Help, approvals, payout, reconciliation — run entirely from native apps, web used only for admin.

### Phase 5 — Web repositioning + native extras

> **Amended 2026-07-19:** the Blazor PWA is **not** shrinking to admin-only. It stays a full client for **non-Apple devices (Android)** — no native Android app is planned; the PWA *is* Android support. The web app has been restyled to the same Graphite & Glass language (theme system rewritten in `design-system.css` §1b: neutral graphite surfaces per mode, themes as accent tints — matching the native apps exactly).

- Web keeps: full PWA (install prompt, service worker, web push for Android), all user-facing screens, plus the admin surfaces (user admin, chore management, achievement manager, planner, query metrics, **printable chore charts**).
- Web sheds only what natives replace *for Apple users*: nothing is removed while it serves as the Android client.
- Native extras, ranked by joy-per-effort: **Home Screen widget** (today's chores / balance) → Lock Screen widget → **App Intents** ("Hey Siri, mark trash done") → Live Activity for day progress → watchOS someday.

---

## 3. Swift architecture notes

- **Toolchain:** you're on **macOS 27 + Xcode 27**, so builds use the iOS 27 SDK — which makes the app **Liquid Glass, full stop** (Xcode 27 ends the pre-26 compatibility look). Set the *deployment target* one notch conservative: **iOS 26 / macOS 26** floor unless the kid's iPhone is already on 27, in which case pin 27 and use the newest APIs freely.
- **Error handling:** typed API errors surfaced as friendly inline states, never raw HTTP. A 401 triggers silent refresh + retry once; refresh failure = clean logout to Login.
- **Testing:** `DailyBreadKit` gets unit tests (token lifecycle, Decimal decoding, date-boundary helpers). UI tests only for the two money paths (complete→approve, payout).
- **Secrets:** none in the app. Server URL + Authentik issuer are config; tokens are Keychain-only.

## 4. Design translation — Guadalupe theme system → native (not a port)

> Note: `DESIGN_SYSTEM.md` in the repo root is stale (still documents the retired Nord palette). Ground truth is `wwwroot/css/design-system.css` §1b: the **Guadalupe theme system** — `data-theme` × `data-mode`, five palettes (Guadalupe/Sea/Garden/Violet/Rose) each with dark + light, and per-mode constants for gold, glow, error, warning, success.

Philosophy: **re-express the identity in the platform's language, don't transliterate CSS.** Liquid Glass gives you materials, depth, and motion for free; Daily Bread's identity rides on top as tint, accent, and its invariant signals. Companion visual: `docs/design-concepts-native.html` — the chosen **"Graphite & Glass"** direction (approved 2026-07-19): system Liquid Glass surfaces in neutral graphite, color demoted to accents (accent = interactive, gold = money, red = Help), dark + light appearances, and the five shipped themes reframed as **accent tints** rather than full palette swaps (`.tint(theme.accent)`).

> Expected tuning: the neutral whites/darks in the mock are approximations of system materials — do a **real-device pass** (the kid's iPhone in daylight, the Mac at night) in Phase 2 before treating any neutral value as final. Keep all neutrals in the `DesignSystem/` layer so this is a one-file tweak.

| Web token world | Native equivalent |
|---|---|
| `--db-bg/*` Guadalupe surfaces (dark `#0F1922` ink · light `#FAFBFC`) | System backgrounds + materials in the asset catalog with dark/light variants — the shipped light/dark mode split maps 1:1 onto native appearance switching. |
| `--db-accent` "Guadalupe" teal-cyan `#4DA8C6` | App accent color (asset catalog `AccentColor`) — the brand anchor, stays. |
| Theme picker (5 palettes × 2 modes) | v1 ships **Guadalupe only**, dark + light. The alternate palettes port later as asset-catalog swaps if wanted. |
| **Invariants: gold = money (`--db-gold`), gold-glow = Approve (`--db-glow`), red = Help (`--db-error`)** | Unchanged, encoded as semantic colors in `DesignSystem/` with the shipped per-mode values — plus native reinforcement: gold-glow gets a success haptic, Help red gets a critical-alert feel. Never themed away. |
| `--ds-space-*`, `--ds-radius-*` | Spacing/radius constants in `DesignSystem` — but defer to platform metrics where they conflict; native rhythm beats imported pixel values. |
| Emoji chore icons (`EmojiConstants`) | **Keep emoji for chore identity** (kid-facing, warm); SF Symbols for all chrome/navigation. |
| Confetti, animated counters, swipeable cards | Native: `TimelineView`/`Canvas` particles, `contentTransition(.numericText)`, native swipe actions. Less code, better feel. |
| Theme guardrails | Same as ever: reverence not kitsch, **no religious iconography**, abstract/elegant only. |

Navigation: iPhone = `TabView` (role-dependent tabs: the kid → Today · Earnings · Goals · Achievements; parents → Dashboard · Approvals · Family · More). iPad/Mac = `NavigationSplitView` sidebar mirroring the web nav's mental model.

## 5. Risks & sharp edges (ordered by likelihood of biting)

1. **Decimal money over JSON** — solved by string-encoding at the contract level (Phase 0). Do not defer.
2. **Day boundary / timezone** — server owns "today"; client passes explicit dates. Pin with tests.
3. **ASWebAuthenticationSession × Authentik × Cloudflare** — should be textbook PKCE, but test the full redirect dance early in Phase 3; keep the kid's password path (which never touches Authentik) as the schedule-protector.
4. **Refresh-token revocation** — new server-side state (hashed token store). Small, but it's auth: integration-test rotation + reuse-detection.
5. **Scope creep on parent admin** — the planner grid and achievement condition builder are weeks of native work for surfaces used at a desk. The admin-stays-web line exists to protect the schedule; hold it.
6. **SignalR Swift client maturity** — official but young; that's why it's optional polish, not a dependency.
7. **$99/yr + Apple review** — TestFlight internal testing has no App Review friction for the family use case; unlisted App Store distribution is a later option, never a blocker.

## 5b. Portability model — the self-hosted-container pattern (decided 2026-07-19)

**Daily Bread is and stays a portable container, never a hosted service.** The maintainer hosts no one's data but his own. The distribution model, if it ever happens, is the **Home Assistant / Jellyfin pattern**: a native app (App Store) that connects to the family's *own* server, plus a container any self-hoster can stand up. That pattern is established and App-Store-approved. The audience is self-hosters, and for them the stack is already nearly turnkey: `docker-compose up` → app + Postgres + auto-migrations; admin bootstraps from env; Authentik optional with local auth fallback.

**What this requires, concretely:**

1. **Server URL field on the app's login screen** (Phase 2, small): text field + `/api/v1/health` reachability check before showing login. Stored per-device; this is *the* portability feature, and it lives in the app, not the DB.
2. **Push for third-party instances is the one hard bit.** APNs pushes must be signed with the developer's `.p8` key (bundle-ID-bound) — it cannot ship in a public container. Options when the time comes: a **stateless push relay** (Home Assistant's model — forwards payloads, stores nothing at rest, hosts no family data) or ntfy for self-hosted instances. *Your* instance uses your own APNs key directly; defer the relay decision until other families are real.
3. **Keep the container turnkey**: migrations on startup stay, all config via env, no manual DB steps, no required external services (Authentik/ntfy remain optional).

**Disciplines from Phase 0 (unchanged, still cheap):** household-scoped API queries; no hardcoded family-shape assumptions in endpoints/DTOs; auth behind the existing `IAuthenticationService` abstraction. These keep the codebase clean regardless of who deploys it.

## 5c. Distribution & transparency (decided 2026-07-19)

- **GitHub hosts everything**: server code, compose files, and DB setup — which is already code (EF migrations apply on container start; the schema is fully auditable in `Migrations/`). App code lives in the same repo (`apps/` or sibling repo, TBD).
- **iOS**: App Store only (no US sideloading path — settled).
- **macOS**: dual-channel — Mac App Store *and* a **Developer ID–signed, notarized DMG** on GitHub Releases. Notarization uses the same $99 account as TestFlight; unsigned direct downloads are a non-starter (Gatekeeper).
- **Public-artifact rule**: no AI/assistant references anywhere public — repo files, docs, app strings, release notes. `CLAUDE.md` stays untracked and is gitignored. Maintainer authors/squashes public commit messages.
- **Privacy rule**: no real family names, no real deployment domains/hostnames, no bundle ids derived from real domains in anything committed. Mock UIs use fictional names; docs use roles ("the kid", "the co-parent", "the maintainer"); domains use example.com placeholders; deployment specifics live in env vars (`PUBLIC_BASE_URL`, etc.), never in code.

## 5d. Onboarding & auth for self-hosted families (decided 2026-07-19)

**Principle: the container is self-sufficient. No external identity provider is ever required.** Authentik remains an optional, env-configured OIDC extra (the maintainer's homelab uses it); no other family needs it. There is no separate "DB API token" concept — the server issues per-user tokens on login (§1 D2), and DB credentials exist only inside the compose file.

- **Built-in identity**: ASP.NET Identity in-container (already present) provides accounts, roles, lockout, **native TOTP 2FA + recovery codes**, and password reset. The security layer lives inside the app.
- **No super-admin.** The **Parent role is the admin role** — full stop. First-run web wizard ("Create your family") replaces the `ADMIN_EMAIL`/`ADMIN_PASSWORD` env bootstrap (env path kept as automation override): whoever completes it becomes Parent #1, then creates the other parent and the kids.
- **Recovery model**: any Parent can reset any family member's credentials **including the other Parent** (mutual parent recovery). Children can reset no one. Both-parents-locked-out break-glass = documented CLI reset via `docker exec` (console access to their own box is the root of trust) + printable recovery codes generated at setup.
- **Native login flow**: server URL → username/password (+ TOTP if enabled) → tokens in Keychain → **Face ID/Touch ID gate** thereafter. Feels like SSO with biometric 2FA; requires zero external services.
- **Device onboarding**: web admin displays a QR (server URL + one-time invite token); the app scans it and lands on login pre-configured.
- **Sharpest self-host edge**: iOS expects HTTPS (ATS). Docs must cover Tailscale's built-in HTTPS certs / reverse-proxy TLS for families running on an old PC + Tailscale. Write this doc page early; it will be the #1 support question.

## 6. Prerequisites checklist

**Before Phase 2 (prototype — free tier):**

- [x] Xcode 27 on the Mac (already installed).
- [ ] Confirm the kid's iPhone iOS version (26 or 27?) → pin deployment target.
- [ ] Free personal team signing in Xcode (automatic); accept 7-day install expiry during prototyping.

**Before Phase 3 (gated on the prototype earning it):**

- [ ] Apple Developer Program enrollment ($99/yr) — allow 1–2 days processing.
- [ ] APNs auth key (`.p8`) generated → backend env vars → compose files.
- [ ] Bundle ID + app group (`group.com.example.dailybread`) registered under the paid team.
- [ ] TestFlight internal group: the maintainer, the co-parent, the kid.

## 7. First working session (Phase 0 kickoff, concrete)

1. Add `Api/` folder: `AuthController` + `ChoresController` (read-only today endpoint) + DTOs.
2. Add JWT bearer scheme + claims-based `ICurrentUserContext` implementation.
3. Add refresh-token table + EF migration.
4. OpenAPI on in Development.
5. Integration tests: password login → token → today's chores for seeded child.
6. Deploy to staging compose; `curl` from the Mac and from a phone on cellular.

---

*References: [official SignalR Swift client](https://github.com/dotnet/signalr-client-swift) · [SignalR Swift client docs](https://learn.microsoft.com/en-us/aspnet/core/signalr/swift-client?view=aspnetcore-10.0) · [Apple SDK minimums (iOS 26 SDK required since April 2026)](https://developer.apple.com/news/?id=ueeok6yw) · [Xcode system requirements](https://developer.apple.com/xcode/system-requirements/)*
