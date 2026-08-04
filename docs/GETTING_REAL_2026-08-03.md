# Getting real — distribution & platform plan (2026-08-03)

The goal, in Shaun's words: a real thing — no "give me your MacBook for 20
minutes", no "son, hand me your phone". That decomposes into five phases,
ordered so each one unlocks the next and nothing waits on Apple until it
has to.

## R1 — The server moves off the laptop (first, no Apple dependencies)

Today the backend is `dotnet run` on a MacBook: the family's app dies when
the lid closes. The repo already carries a Dockerfile, compose files, and
deploy.sh — the missing piece is a home.

- **Host on the Unraid box** (Docker: API container + Postgres container,
  appdata-backed volume so Unraid's backup covers the family's data).
- **Expose through the existing Cloudflare Tunnel** as
  `https://<subdomain>.<domain>` — TLS for free, no port forwarding, and the
  app's connect field finally gets to keep its `https://`.
- **Before exposure**: a security pass (SECURITY.md review, login rate
  limiting, JWT secret from env not appsettings, confirm the driving CSV and
  themes endpoints enforce auth), plus a DB backup job.
- **Updates**: start with deploy.sh by hand; graduate to image-build on push
  + auto-pull when that gets old.

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
