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
     name ("Daily Bread" is taken by the devotional — the phone's icon
     label stays "Daily Bread" from `CFBundleDisplayName` regardless),
     Bundle ID `com.jshauns.dailybread` (the explicit one, never a
     wildcard), SKU `dailybread`, Full Access.
   - **Done 2026-08-05:** record created as **"Daily Bread - Chore
     Tracker"**, iOS + macOS platforms both on the record (the Mac side
     stays dormant until the R3 sandbox decision). The main App ID turned
     out to already exist — Xcode had auto-registered it (the `XC` prefix
     in the dropdown) — so only the app group and the widgets ID needed
     checking by hand.
2. App page → **Xcode Cloud** tab → edit the workflow: the action must be
   **Archive — iOS** (not just Build) with deployment preparation
   **TestFlight (Internal Testing Only)**, plus a post-action **TestFlight
   Internal Testing** pointed at the tester group from step 3.
   The workflow's **Environment** can stay on **Latest Release** — see the
   root-cause below. Cloud manages build numbers itself.

   **The actool crash, root-caused 2026-08-05** (kills the "argument-order"
   theory of 2026-08-03): every Cloud build since #72 died with
   `attempt to insert nil object` because the icon was authored in the
   **Xcode 27 beta's Icon Composer**, which writes two things stable
   Xcode 26 cannot parse — `"specular": "inside"/"outside"` (26 expects a
   boolean) and a top-level `"features"` array. Stable actool maps them to
   nil and crashes in the `--output-partial-info-plist` path, which is why
   bare actool runs pass and xcodebuild runs die. Found by local bisection
   against stable 26.6 (17F113), the exact build Cloud runs; 18 actool
   invocations, three poisons, one shared root. icon.json now stays in the
   26-compatible dialect (boolean specular, no features array, refractivity
   tuning intact — that part parses fine everywhere). **Rule until Cloud
   offers Xcode 27: when editing the icon in Icon Composer, set the
   document type to the iOS 26 format before saving.**
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

## R3 — The Mac (the wife's 75% surface) ✅ SHIPPED & INSTALLED 2026-08-06

**Verified end-to-end 2026-08-06:** build 106's Mac archive passed ITMS
validation clean (build 100's two rejections both answered), and the
native sandboxed Mac app installed on Shaun's Mac via TestFlight. One
gotcha for the record: the first build of a new platform needed a one-time
add to the internal group in the TestFlight tab's macOS section — until
then, TestFlight on the Mac only offers the iOS-on-Apple-silicon variant,
which looks like "the iOS version is persisting."

**Shaun chose road (a): sandbox it** — same evening build 100 put iOS on
TestFlight and the Mac archive bounced with ITMS-90296 (sandbox required)
+ ITMS-90242 (missing category). What shipped:

- `Config/DailyBread-macOS.entitlements` (committed, hand-written):
  `app-sandbox` + `network.client` + `files.user-selected.read-write`,
  nothing else. iOS keeps its generated entitlements (widget app group);
  the two platforms now have deliberately different entitlement files.
- `LSApplicationCategoryType: public.app-category.productivity`.
- `container-migration.plist` in the Mac bundle: moves
  `~/Library/Application Support/DailyBread` (Themes/) into the container
  on first sandboxed launch — the automatic migration only handles
  bundle-id-named paths, and this folder isn't one.

Migration story for existing Macs: preferences (server URL, theme picks)
migrate automatically; Keychain sessions survive (same signing identity);
themes move via the manifest, and they're server-synced anyway, so the
worst case is a re-sync. First locally-run sandboxed build performs the
move — after that, the old unsandboxed folder is no longer consulted.

Rejected road (b), Developer ID + Sparkle: a second update mechanism to
own, preserving freedoms (shell-out, open filesystem) this plain SwiftUI
client never uses.

## R4 — Face ID (app feature, after distribution works) ✅ BUILT & VERIFIED 2026-08-06

Two distinct jobs, both LocalAuthentication:

- **Biometric gate on parent surfaces** — the real win on shared devices:
  approvals, money, settings guarded by Face ID/Touch ID instead of nothing.
- **Biometric-protected refresh token** (`kSecAccessControl` /
  biometryCurrentSet) — turns "the phone was unlocked" into "the owner was
  present" for the session itself. Optional per user; a kid's own phone can
  stay frictionless.

Needs only NSFaceIDUsageDescription — no entitlement, no server work.

### Built 2026-08-06 — decisions and why

The threat model, stated so the code can be judged against it: a bright child
holding a parent's already-unlocked phone, who has watched the device passcode
typed a hundred times, and who gets another try every release. What ships is a
**local presence lock on an already-authenticated session, not an
authorization boundary** — the server's JWT roles remain the real one.
(`Daily_Bread/Services/BiometricAuthService.cs` is dead WebAuthn scaffolding
with a confusingly adjacent name. Unrelated, no callers, untouched.)

**Biometry only where biometry is enrolled; the device passcode deliberately
refused.** `authenticate` evaluates `.deviceOwnerAuthenticationWithBiometrics`
with `localizedFallbackTitle = ""`, so no "Enter Passcode" button renders.
Accepting the passcode would make the gate decorative against the one
adversary it exists for, and would let a child fail Face ID five times on
purpose and walk in. It is also forced by job 2: a `.biometryCurrentSet` item
is not satisfied by a context evaluated for `.deviceOwnerAuthentication`.
`.deviceOwnerAuthentication` is used on exactly one path — a Mac or iPhone with
a password but no enrolled biometry, where the gate defaults **off**.

The stranding risk that refusal creates is **accepted**: a parent whose sensor
fails is not locked out, because "Sign in with your password" and "Change
server" are drawn on every state of the wall and are never gated. The account
password is the stronger credential, so a griefing child who signs a parent out
costs one password entry and gains no capability.

**Gate default-on for parents with enrolled biometry**, with an explanatory
first-encounter panel ("Parent screens are protected") that does **not**
auto-prompt, and with **no skip button**. Between a TestFlight update
installing and the parent first opening the app there is a window in which a
child could tap "Not now"; a one-tap permanent disable sitting on an unlocked
phone is the exact hole the feature closes. The way off is inside, past a
match, in Settings → Security. `.ownerPasscodeOnly` and no-passcode devices
default off; a device with no passcode never engages the gate at all.

**`.biometryCurrentSet` over `.userPresence`.** `.userPresence` accepts the
device passcode, which in this family makes job 2 a no-op. `.biometryCurrentSet`
alone, with no `.or, .devicePasscode` leg, plus
`kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly` so the item stays out of
backups, off a restored device, and evaporates if the passcode is removed. The
consequence — a new face or finger invalidates the item — is announced in the
Settings footer *before* the switch is flipped, and surfaces as one explained
re-login, never a silent logout.

**Job 2 is iOS-only.** The blocker is macOS, not the design: nothing today sets
`kSecUseDataProtectionKeychain`, so the Mac's generic-password items live in
the file-based keychain, where the iOS `SecAccessControl` semantics do not
apply. Opting into the data-protection keychain on macOS needs a
`keychain-access-groups` entitlement, and moving existing items into it is a
migration, not a flag flip. The Mac app has been sandboxed since R3a on Shaun's
sign-off; **lifting this is gated on his sign-off too**, and is not R4's to
take. The flag is set explicitly in `ProtectedKeychain` anyway so it is already
in the right place if that day comes.

**The wall sits above `MainView`, in `RootView`.** `MainView` is never
constructed while locked, so its `.task`, `.poll` and `.refreshOnForeground`
never run — the 30 s Approvals badge poll stops hitting the network and none of
the app's own views render (the Home-screen widget is a separate process and
keeps showing its last snapshot; see the correction at the end of this
section). Pushed `NavigationStack` destinations cannot
outlive a re-lock, presented sheets go with the subtree, and ⌘1…⌘N reach
nothing. The decisive argument is that **`ParentHomeView` is a hub, not a
leaf**: it reaches `ActivityView(userId:)` (another child's day, fully
mutable), `RewardClaimsView(mode: .parent)` (approving credits cash),
`CalendarView(userId:)`, `DrivingLogView(mode: .parent)` and
`AdjustBalanceSheet`. Gating destinations individually means enumerating those
and re-enumerating them every release. Deep links, App Intents and `onOpenURL`
do not exist today; when one is added it lands above the wall and is gated by
construction. **The one gap the seam does not close for free**, recorded in
`DailyBreadApp.swift`: there is no macOS `Settings` scene and no `.commands`
today, and either would be a second door onto `SettingsView` needing its own
`isLocked` check.

**Two `APIClient` fixes landed first, before any biometric code existed.** Both
were live bugs whose symptoms would have read as Face ID bugs once R4 shipped.
(a) `refreshTokens()` had no in-flight dedupe, so two concurrent 401s both
presented the same refresh token and `ApiTokenService.RefreshAsync` treated the
loser as theft and revoked every token the user owns — one race signed the
family out on all their devices. (b) The refresh catch was unconditional, so a
Wi-Fi blip fired `onSessionExpired()` and deleted the Keychain tokens; only a
rejection from `/auth/refresh` ends a session now, and `APIError.network`
rethrows untouched.

**Operational note, not code: iOS Password AutoFill.** The password escape
hatch is only as strong as whether the Daily Bread password sits in a shared
iCloud Keychain — AutoFill will fill it behind the device passcode, which the
child knows. This vector is **not created by this feature**: the same
credential signs in to the web app at dailybread.simmserv.org from any browser.
It is not closable in the client, so it belongs here as a household practice —
keep parents' Daily Bread passwords out of any keychain the kids' devices
share.

Step-up: `ResetPasswordSheet.save()` requires a fresh match (30 s window)
because Rename and Reset password are the only row actions not self-excluded,
so resetting a *parent's* password is a full account takeover that outlives the
grant that opened the wall. `AdjustBalanceSheet` deliberately does **not** step
up — taxing routine money edits is how a security feature gets switched off.

### Corrected 2026-08-06, after adversarial review — where the spec was wrong

Thirteen blocking defects came out of review. Four of them were the spec's own
instructions, and those are the ones worth recording, because the code now
deliberately departs from the design above.

**The default-on gate is written down, not inferred.** The spec had
`db.parentGate.<userId>` absent meaning "default by capability", with `setEnabled`
the only writer. That made the whole feature evaporate on a capability downgrade:
the child who knows the passcode opens Settings → Face ID & Passcode → Reset
Face ID, `isEnabled` falls back through the table to false, and the wall never
renders again — no prompt, no credential, no signal. `bind(to:)` and a first
successful `unlock` now materialise an explicit `true`. Only the `true` answer is
written; storing an explicit `false` for a device with nothing enrolled would
freeze it off, so enrolling Face ID later would no longer arm the gate.

**Fail-open on `capability == .none` is now conditional.** §7.2 had the gate
disengage outright on a device with no passcode. That made *removing the
passcode* — four taps, with a passcode the adversary is assumed to know — the
cheapest route through the wall, cheaper than any other, and silent. A device
that never hosted the gate still fails open; a device where the gate was
explicitly on keeps the wall, `authenticate` answers `.unavailable`, the wall
says so in words, and the password sign-in underneath it is the way back in.

**"Every trigger is a no-op while `authenticating`" was wrong for `.background`.**
It is right for `.inactive`, which the Face ID sheet raises itself. On
`.background` it turned the strongest re-lock trigger in the design into a
fail-open, and the feature opened the window itself: a parent who swipes home
while the Settings rehearsal or the password-reset step-up is up backgrounds the
app with the grant still open and no cover, LocalAuthentication cancels, and
nothing re-examines the phase afterwards. Locking under a live prompt costs
nothing, because the wall is what you come back to either way. `.active` also
clears the cover unconditionally now — guarded, a cover raised before an
evaluation sat over a live app with nothing left to remove it.

**`policy.idleGrace` was not an idle timer.** Nothing called `touch()`, so the
grace was an absolute grant lifetime that expired mid-use: five minutes into a
run through Approvals the shell was torn down mid-scroll, taking `MainView`'s
`selection` with it, so the parent also lost their place. `RootView` now reports
activity with a zero-distance `simultaneousGesture` over `MainView` — it fires on
any touch without consuming it — and the expiry task loops on a `lastActivity`
deadline instead of sleeping once, so reporting activity is one stored write.

Four more corrections worth naming, all in the same spirit — the mechanism was
right and the edges were not. The `ProtectedKeychain.write` rotation path deleted
the sealed item before adding its replacement, and the item is unwritable while
the device is locked, so a rotation landing in the runway between the screen
locking and the app suspending destroyed the only copy of the refresh token; the
add is attempted first now and the delete only happens on `errSecDuplicateItem`,
and `persistRotated` checks the status instead of discarding it — on failure the
session degrades to the plain `AfterFirstUnlock` items and says so in Settings
rather than dying or lying. `enableProtectedSession` / `disableProtectedSession`
read the tokens before the biometric sheet and wrote them after, so a rotation
landing during those seconds sealed a token the server had already revoked, which
it answers by signing out every device the household owns; both now reconcile
against the last rotated pair without an intervening `await`. The step-up in
`ResetPasswordSheet` was unconditional, which made resetting a password
*impossible* on a device the gate never engages on — it takes `ifEngagedFor:`
now. And the privacy cover was applied to every session including children's, so
on the kitchen iPad in Split View a child's chore list was replaced by the splash
the moment they touched the app beside it; `isCovered` is only ever set for a
parent past the wall. A parent's own session in Split View still covers when
unfocused, which is the feature working rather than a bug.

### What a parent sees when it goes wrong

Every failure resolves to an inline row under the Unlock button, never an alert
— the app has exactly one alert and this does not become its second — and
"Sign in with your password" and "Change server" are drawn under every one of
these states, ungated. There is no state in which a parent is stuck.

A **non-match** says "That didn't match. Try again." and leaves the button live.
A **cancel** says nothing at all: dismissing a sheet is a choice, and an error
row that fires every time somebody dismisses one teaches people to ignore error
rows. Cancelling also deliberately does not re-arm the auto-prompt, because
auto-prompting on cancel is a loop with no way out. **Five failures** (Apple's
counter, not ours) gives "Face ID is locked. Unlock your iPhone with its
passcode, then try again." — recovery is the OS's own, and there is no in-app
passcode button precisely because that would let a child fail five times on
purpose and walk in. A device that **cannot evaluate at all** — no biometry
enrolled, the gate explicitly on — says "This device can't check who you are.
Sign in with your password.", which is the fail-closed answer that keeps
removing the passcode from being the cheapest way through. Any **other LAError**
lands on "Couldn't check who you are. Try again, or sign in with your password."

Two failures are quiet on purpose. A **`.biometryCurrentSet` invalidation**
(job 2, someone enrolled a new face or finger) shows no error row; it surfaces
as one re-login, and the Settings footer states that consequence *before* the
switch is flipped rather than after. And if the sealed-item write fails during
a token rotation, the session **degrades to the plain `AfterFirstUnlock` items
and says so in Settings** — it does not die, and it does not claim protection
it no longer has.

### Verified 2026-08-06 — the gate, and what it actually said

Regenerated from `project.yml` and confirmed landing inside the worktree
(`Created project at …/worktrees/daily-bread-macos-ios-status-012ba0/apps/DailyBread/DailyBread.xcodeproj`),
then built both slices unsigned: **iOS `generic/platform=iOS` BUILD SUCCEEDED**
and **macOS `platform=macOS,arch=arm64` BUILD SUCCEEDED**. The usage string was
checked in the *built* bundle rather than in the spec —
`plutil -p …/Debug-iphoneos/DailyBread.app/Info.plist` prints
`NSFaceIDUsageDescription`, so the key survives generation into the product.
`git diff` on the committed `apps/DailyBread/DailyBread/Info.plist` after
regeneration is exactly the two intentional lines and nothing else, which is
the check that matters here: xcodegen owns that file, so drift between it and
the spec would be silently overwritten on the next generate.

Tests: **221 passed / 0 failed** in `Daily_Bread.Tests`, unchanged and expected
— R4 is client-only and touches no server code. **74 passed / 0 failed** in
`DailyBreadKit`, of which **42 are the new `ParentGateTests`** covering the
capability table, the default-on materialisation, the conditional fail-open,
and the re-lock triggers.

What is deliberately **not** in this: job 2 stays **iOS-only** until Shaun
signs off on the `keychain-access-groups` entitlement the Mac would need to
opt into the data-protection keychain, plus the item migration that follows —
the flag is already set in `ProtectedKeychain` for the day that happens. The
macOS `Settings` scene and `.commands` remain **unbuilt**, which is what keeps
`SettingsView` behind a single door; adding either needs its own `isLocked`
check, and that is recorded in `DailyBreadApp.swift` where somebody would trip
over it. `AdjustBalanceSheet` takes **no step-up** by design, because taxing
routine money edits is how a security feature gets switched off. And the
iCloud Keychain AutoFill vector is a **household practice, not a code fix** —
the same password signs in to the web app from any browser.

**What this verification did NOT cover, stated plainly so nobody reads the
green checks as more than they are: no biometric path has been executed on any
device or simulator.** Every `LocalAuthentication` and protected-Keychain line
is compile-verified only; the 42 gate tests drive injected closures, never real
hardware. Face ID enrollment, the five-failure lockout, and
`.biometryCurrentSet` invalidation are OS behaviours a unit test can only stub.
`testParentGateWall` exists but has never run — it skips on a signed-out
simulator, and its own failure message admits it cannot distinguish "gate
broken" from "biometry not enrolled". The first real Face ID prompt in this
project's history will happen on Shaun's phone.

Two more honest edges. `SessionStore` is untestable by construction —
`Keychain` and `ProtectedKeychain` are static enums with no injection seam — so
the repairs most likely to strand a session (the add-before-delete write, the
rotation degrade path, the `lastRotated` reconciliation) carry no tests. And
`APIClient` has no test file at all, though its two fixes change session
lifetime for **every** user including the kids.

### Corrected 2026-08-06, after the completeness pass — macOS re-lock

The first implementation claimed in a code comment that "locking your Mac locks
parent mode" while observing only `screensDidSleep` (display sleep) and
`sessionDidResignActive` (fast user switching). Neither fires for ⌃⌘Q or a hot
corner with the display still lit — the actual walk-away gesture. The
distributed `com.apple.screenIsLocked` notification is now observed alongside
both, and `NSWindow.willCloseNotification` (filtered to real windows, since
sheets and panels are NSWindows too) hard-locks on ⌘W — closing the last window
does not end the process, so reopening from the Dock used to land back inside
an unlocked shell. This mattered more than it looks: the Mac is Charmaine's
75% surface.

Both are wired and both slices still build; like everything else here, neither
has been exercised against a real screen lock yet.

One overstatement corrected in the same pass: "nothing renders to be
screenshotted" is true of the app's own views, but the **Home-screen widget
keeps rendering its last snapshot** — balance, streak, today's earnings, child
name — from the app-group file regardless of the gate. That is pre-existing and
read-only, not something R4 introduced, but it is not nothing.

### The sign-in loop, root-caused 2026-08-07 — and it was never R4

Shaun's first evening on the R4 build: "the auto/token/cache is simply not
retaining auth between sessions", on **both** devices, with and without job 2.
He also said it had been happening for a while — which was right, and the
reason is on the server, not in any of the code above.

`ApiTokenService.RefreshAsync` treated any already-rotated refresh token as
theft and called `RevokeAllForUserAsync`, which revoked **every active token
the user owned on every device, including ones issued seconds earlier**. That
turns one stale token into a self-sustaining loop:

1. Some device presents a rotated token — a raced refresh, a retried request,
   or a reply that arrived after the app was killed, all ordinary on a phone.
2. The server revokes everything. The Mac *and* the phone are signed out.
3. He signs in on the phone. Fresh token.
4. The Mac, still holding its dead token, refreshes — and revokes the phone's
   brand-new session. Back to 3, forever.

Production had **28 of these revocations in 24 hours**, all on his account. The
`RefreshTokens` rows show the original trigger plainly: six tokens created in
the same second (14:58:06 on 2026-08-06), which is one client firing six
concurrent refreshes — the missing in-flight dedupe R4 had already fixed
client-side. But fixing the client only stops *that* device racing; the loop
needed the server to stop being able to sustain it.

Two changes, both in `ApiTokenService`:

- **A 60-second grace window** (`Api:Jwt:ReuseGraceSeconds`). A token replayed
  within it is a client that never got the reply, so the chain is followed to
  its replacement and *that* is rotated — the client comes back in sync instead
  of being signed out. Possession was proven legitimate seconds earlier.
- **Containment no longer reaches forward in time.** Genuine reuse revokes only
  tokens created at or before the compromised token's own revocation. A session
  started after the leak cannot be the leak, and that single clause is what
  makes the loop impossible.

Three tests pin it, including `Refresh_Reuse_Leaves_A_Session_Created_After_The_Leak_Alone`.
The old test asserted the buggy behaviour ("revokes everything, including the
fresh one") and was replaced. 223 pass. **This is a server fix: it needs a
deploy, not a TestFlight build.**

### Also 2026-08-07 — the Planner taps R4 broke

Same evening: the Planner's New, mode-switch and Edit controls, plus the task
and routine rows, needed two or three taps. The cause was R4's own idle-timer
plumbing — `.simultaneousGesture(DragGesture(minimumDistance: 0))` over the
whole shell, whose comment claimed buttons underneath "behave exactly as they
did". They did not: SwiftUI still arbitrates that drag against every button
below it. The completeness critic had flagged this exact modifier as the change
most likely to hurt, before Shaun ever saw it.

Replaced by `ActivityReporter`, which observes one layer down where it cannot
compete: on iOS a `UIGestureRecognizer` that fails itself the instant it sees a
touch (so it never delays, cancels or consumes one), on macOS a passive
`NSEvent` local monitor that returns every event unchanged. The idle timer
still gets fed; the buttons never know.

### The "Mac password" prompts are codesign, not the app (2026-08-07)

Corrected same day: the first guess here was a runtime keychain ACL problem
from the R3a sandbox. Wrong. Shaun described the prompt as asking to **sign**
something, naming `com.jshauns.dailybread`, appearing "three or four" times,
and happening long before any of this — which is `codesign` reaching for the
signing identity's private key during an **Xcode build**, once per signed
binary (app, then widget extension, then embedded frameworks). Nothing to do
with the app at runtime.

The fix is a keychain ACL choice that only Shaun can make, and the standing
signing rules mean nobody else touches it: answer that prompt with **Always
Allow** rather than Allow. Allow authorises exactly one use, so every build
asks again. Note there are **two** valid `Apple Development` certificates in
his login keychain (`jshauns@gmail.com` and `Jimmie Simmons`, both
`OU=722W7866NQ`, so both correct for this project) — each has its own private
key with its own ACL, so whichever Xcode selects has to be granted separately.
Consolidating to one is his call, not a repo change.

What the login keychain also shows, read-only: three orphaned items under
`com.example.dailybread.tokens`, the service name used before d080080 renamed
it to `org.dailybread.tokens`. Dead weight from a pre-rename build. Harmless
unless an old locally-built copy of the app is ever launched, in which case it
would reach for them and could prompt. Left in place — deleting items from
Shaun's keychain is his call.

And: `org.dailybread.tokens` is **absent entirely** from the login keychain,
which is consistent with the Mac having no saved session — exactly the reported
symptom. It does not by itself prove writes are failing, because he was signed
out when this was checked. The decisive test is one sign-in: if the item then
exists, writes are fine and the sign-in loop above was the whole story.

Not yet on TestFlight: shipping is a push to master, and that is Shaun's call.

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
