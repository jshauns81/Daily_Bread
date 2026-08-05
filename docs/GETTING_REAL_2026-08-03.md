# Getting real — distribution & platform plan (2026-08-03)

The goal, in Shaun's words: a real thing — no "give me your MacBook for 20
minutes", no "son, hand me your phone". That decomposes into five phases,
ordered so each one unlocks the next and nothing waits on Apple until it
has to.

## R1 — The server moves off the laptop (first, no Apple dependencies)

**Revised 2026-08-04 — the home already exists.** Production runs today at
`https://dailybread.simmserv.org`: Unraid, the repo's own compose file
(app container built from source + postgres:16 on an appdata volume,
DataProtection keys persisted), exposed through the existing Cloudflare
Tunnel. What's deployed is the pre-API web-app build — `/api/v1/health`
302s to the Blazor login — so R1 is a **refresh**, not a build-out.

Verified ahead of deploy day (2026-08-04): current master's image builds
clean (`docker build .`, .NET 9, 453MB), `Program` still accepts the
compose file's `DATABASE_URL` form, startup runs `MigrateAsync()` so the
schema upgrade is automatic, and compose now REQUIRES `JWT_SIGNING_KEY`
(without it every phone signs out on every redeploy; the compose var
fails loud instead of silently minting an ephemeral key).

Deploy day, on the Unraid console (`/mnt/user/appdata/Daily_Bread`):

1. `mkdir -p backups && docker exec dailybread-postgres pg_dump -U dailybread dailybread > backups/pre-native-$(date +%F).sql`
2. `echo "JWT_SIGNING_KEY=$(openssl rand -base64 48)" >> .env` (once, forever)
3. `./deploy.sh rebuild --verbose` (pulls master, rebuilds, migrations run at boot)
4. `curl https://dailybread.simmserv.org/api/v1/health` → `Healthy`
5. Each device: connect screen → `dailybread.simmserv.org` → sign in.
   Web logins survive (cookie keys persist in the /keys volume).

Still worth doing soon after: the security pass (SECURITY.md review, login
rate limiting, confirm driving CSV + themes endpoints enforce auth) and a
scheduled backup job around step 1's pattern.

## R2 — iPhones get the app through TestFlight (the no-cables answer)

Xcode Cloud already builds this repo (fixed 2026-08-02: post-clone xcodegen
+ shared scheme), which means the paid developer program is active — so
TestFlight is available today.

- One-time in App Store Connect (Shaun's Apple ID, ~20 min): create the app
  record for `com.jshauns.dailybread`, make an internal-tester group, invite
  the family's Apple IDs; everyone installs the TestFlight app once.
- Wire the Xcode Cloud workflow: push to master → build → TestFlight
  internal group. From then on updates PUSH: Shaun merges, phones update
  themselves. No cables, ever.
- The workflow must select the **beta Xcode version**: stable Xcode's actool
  crashes compiling the Icon Composer app icon (argument-order bug, confirmed
  2026-08-03 by bisection; beta compiles it in any order). Shaun builds with
  Xcode-beta locally, so Cloud has to match.
- TestFlight builds expire after 90 days — irrelevant while we're iterating
  weekly. The endgame (App Store unlisted/private distribution) is a later
  decision, not a blocker.

## R3 — The Mac (the wife's 75% surface)

TestFlight for Mac requires App Sandbox, and the Mac app is deliberately
unsandboxed today. Two roads:

a) **Sandbox it** and ride the same TestFlight train as iOS. Likely cheap:
   themes live in the app's own container, exports go through the user-picked
   save panel — both sandbox-native patterns. Needs an entitlements change,
   which is gated on Shaun's explicit sign-off by standing rule.
b) **Developer ID + notarized download + Sparkle auto-updates.** No sandbox,
   but a whole second update mechanism to own.

Recommendation: evaluate (a) first; only fall back to (b) if sandboxing
breaks something real.

## R4 — Face ID (app feature, after distribution works)

Two distinct jobs, both LocalAuthentication:

- **Biometric gate on parent surfaces** — the real win on shared devices:
  approvals, money, settings guarded by Face ID/Touch ID instead of nothing.
- **Biometric-protected refresh token** (`kSecAccessControl` /
  biometryCurrentSet) — turns "the phone was unlocked" into "the owner was
  present" for the session itself. Optional per user; a kid's own phone can
  stay frictionless.

Needs only NSFaceIDUsageDescription — no entitlement, no server work.

## R5 — Notifications & messaging (last, biggest server lift)

"Messaging" splits into two products; decide separately:

- **Push notifications (APNs)** — the obvious wins: "Emma finished
  everything today", "a drive is waiting for your approval", "you got paid".
  Server-side: device-token registry + APNs HTTP/2 sender (needs a .p8 key
  from App Store Connect). App-side: registration + notification categories.
- **In-app nudges/messages** ("remind Noah about the dishwasher") — a
  product-design conversation before it's code. Not sketched yet on purpose.

Widgets already ship; Live Activities (a chore-day progress activity) become
possible once APNs exists.

## What only Shaun can do

1. Pick the subdomain/domain for the tunnel (R1).
2. App Store Connect clicks: app record, tester invites, the .p8 key later
   (R2, R5) — guided, but it's his Apple ID.
3. Sign off on the sandbox entitlements change, or veto it (R3).
4. Confirm the kids' Apple IDs exist for TestFlight invites (R2).
