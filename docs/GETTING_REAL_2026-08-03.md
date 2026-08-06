# Getting real — distribution & platform plan (2026-08-03)

The goal, in Shaun's words: a real thing — no "give me your MacBook for 20
minutes", no "son, hand me your phone". That decomposes into five phases,
ordered so each one unlocks the next and nothing waits on Apple until it
has to.

## R1 — The server moves off the laptop ✅ SHIPPED 2026-08-04, 21:56

Deployed by Shaun via `./deploy.sh rebuild` after the rehearsal below.
Verified live: `/api/v1/health` → Healthy, protected API → 401 (JWT
challenge, was a 302 to Blazor login), web app → 200 untouched. The
box's uncommitted local work survives as `archive/unraid-local-2026-08-04`;
its two good ideas (pre-migrate auto-backup in deploy.sh, unpublished
5432) were ported to master first. JWT_SIGNING_KEY is set, so phone
sessions survive future redeploys. Native devices connect by typing
`dailybread.simmserv.org` on the connect screen.

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

**Repo preflight ✅ done 2026-08-05** — what upload validation checks is
already in the tree:

- Privacy manifests (`PrivacyInfo.xcprivacy`) in the app **and** the widget
  extension: `UserDefaults` declared with reason CA92.1, tracking `false`,
  nothing collected. This is what ITMS-91053 emails are about.
- `ITSAppUsesNonExemptEncryption: false` was already in Info.plist — no
  export-compliance question on every build.
- The 1024 marketing icon exists in the classic fallback set; the Icon
  Composer icon carries its own. No privacy-permission strings needed —
  the app uses no camera, location, or photo APIs.
- The arm64 **device** slice compiles (first time it was ever built —
  everything before ran on simulators and the Mac). Verified with
  `CODE_SIGNING_ALLOWED=NO`; local signing state untouched. Xcode Cloud
  signs in the cloud with ASC-managed certs — nothing on the Mac changes.
- `ci_scripts/ci_post_clone.sh` still generates the project on Cloud.

**The strategy: everyone is an INTERNAL tester.** Internal builds skip Beta
App Review entirely — no reviewer needs a demo account on the family's real
server, nothing is publicly linkable, and every green build is on the phones
minutes after Cloud finishes. External groups/public links exist for
strangers; a family doesn't need them.

**Shaun's App Store Connect session (~20 min, his Apple ID):**

1. **Reality check 2026-08-05: there was no app record** — My Apps was
   empty, so the 2026-08-02 Cloud fix never fully onboarded, and the
   identifiers weren't registered either (simulator and unsandboxed-Mac
   builds never register App IDs). The working order:
   - developer.apple.com → Identifiers: register the **App Group**
     `group.org.dailybread.shared`, then explicit App IDs
     `com.jshauns.dailybread` and `com.jshauns.dailybread.widgets`, each
     with the App Groups capability assigned to that group.
   - App Store Connect → Add Apps → **New App**: iOS, a globally-unique
     name ("Daily Bread" is taken by the devotional — any placeholder
     works; the phone's icon label stays "Daily Bread" from
     `CFBundleDisplayName`, and the store name is changeable long before
     any App Store release), Bundle ID `com.jshauns.dailybread` (the
     explicit one, never a wildcard), SKU `dailybread`, Full Access.
2. App page → **Xcode Cloud** tab → edit the workflow: the action must be
   **Archive — iOS** (not just Build) with deployment preparation
   **TestFlight (Internal Testing Only)**, plus a post-action **TestFlight
   Internal Testing** pointed at the tester group from step 3.
   In the workflow's **Environment**, select the **beta Xcode** — stable
   actool crashes on the Icon Composer icon (argument-order bug, confirmed
   2026-08-03 by bisection). Cloud manages build numbers itself.
3. App page → **TestFlight** tab → Internal Testing → **+** → group
   "Family". Add Shaun. To add Charmaine: **Users and Access → +** first
   (any modest role — Customer Support is fine, tick "internal tester"),
   then add her to the group. Kids' phones don't need their own invites at
   all: install TestFlight on the kid's phone and **sign TestFlight itself
   into a parent's Apple ID** — TestFlight's sign-in is separate from the
   device's App Store account, and one Apple ID can run TestFlight on many
   devices. (If a kid's Apple ID is 13+, inviting it directly also works.)
4. Start a build: press **Start Build** in the workflow, or just push to
   master. ~20 min later the build lands in the internal group and phones
   get the install/update banner in TestFlight.
5. Each phone, once: install **TestFlight** from the App Store → accept →
   install Daily Bread → connect screen → `dailybread.simmserv.org` →
   sign in. From then on updates push themselves.

TestFlight builds expire after 90 days — irrelevant while we iterate
weekly; any new master push resets the clock. The endgame (App Store
unlisted/private distribution) is a later decision, not a blocker.

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
