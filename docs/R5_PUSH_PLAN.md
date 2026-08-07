# R5 — APNs push notifications (architecture plan, 2026-08-06)

Design document for phase R5 of `GETTING_REAL_2026-08-03.md`. Nothing here is
built yet. It exists so the session that builds it does not have to re-derive
any of it.

Scope: **push notifications only**. The other half of R5 in the parent doc —
in-app nudges ("remind Noah about the dishwasher") — is a product-design
conversation and stays unsketched on purpose.

---

## 0. The shape of the problem, stated once

Three notification channels exist today and **none of them reach the native
app**:

- **SignalR** (`Hubs/ChoreHub.cs`, `Services/ChoreNotificationService.cs`) —
  live in-app updates, Blazor-only client. Five events. Works.
- **Web Push / VAPID** (`Services/PushNotificationService.cs`) — registered
  only from `Components/Shared/PushNotificationToggle.razor`; no controller
  touches it, so a native client cannot subscribe. One content-bearing send
  exists (help request).
- **ntfy** (`Services/NtfyAlertService.cs`) — the only channel that reliably
  lands on a phone today. One caller: help requests, `TrackerService.cs:820`.
  Deployment-wide by construction (one `NTFY_TOPIC`).

The native app compensates with a 30-second `.poll` and a foreground refresh
(`DesignSystem.swift`). That covers "app is open". It cannot cover "phone is
in a pocket", which is the entire point of R5.

**Two facts that shape every decision below:**

1. **Fan-out is not household-scoped.** `SECURITY_AUDIT_2026-08-04.md:58-62`
   recorded this as deferred. `PushNotificationService.SendToRoleAsync`
   enumerates *every* user in the `Parent` role in the whole database;
   `ChoreHub.ParentsGroup` is one process-wide group keyed on role alone. It
   has not bitten because one household exists. Copying either shape for APNs
   reproduces the leak on a surface far worse than best-effort web push — a
   lock screen, with a child's name and a free-text help reason on it. **The
   fix goes in first, before any sender exists** (Phase 0).

2. **R4 just built a biometric wall over parent surfaces.** A notification is a
   door into the app that did not exist when that wall was designed. Two
   consequences, both load-bearing: no notification action may move money
   (§7.2), and a deep link that arrives while `SessionStore.state == .locked`
   must be *held*, not applied (§8.4).

---

## 1. The .p8 key — what Shaun creates and where it lives

### 1.1 Creating it

App Store Connect → **Users and Access** → **Integrations** → **Keys** →
**Apple Push Notification service (APNs)** → **+**.

- Name it `Daily Bread APNs`.
- **Restrict it to the `com.jshauns.dailybread` bundle ID**, not team-wide.
  Least privilege: a team-wide key can push to any app on the account. The only
  cost of restriction is minting a new key if the bundle ID ever changes, which
  it will not.
- Download `AuthKey_XXXXXXXXXX.p8`. **Apple allows this exactly once.** If it
  is lost, the key is revoked and remade — there is no recovery.
- Record two values from the same page: the **Key ID** (the 10 characters in
  the filename) and the **Team ID** (`722W7866NQ`, already in
  `apps/DailyBread/project.yml:107` as `DEVELOPMENT_TEAM`).

Separately, in **Certificates, Identifiers & Profiles → Identifiers →
com.jshauns.dailybread**, enable the **Push Notifications** capability. Without
this the `aps-environment` entitlement will not provision and Xcode Cloud
archives fail at signing, not at build — a confusing place to discover it.

### 1.2 Where it goes

**Never in git.** `.gitignore` already covers `*.key` and `*token*` but **not
`*.p8`** — the first commit of this phase adds `*.p8` to `.gitignore`. That is
belt and braces; the key should never be inside the repo tree at all.

The key is delivered as an **environment variable, base64-encoded**, in
`/mnt/user/appdata/Daily_Bread/.env` on Unraid — the same file that already
holds `JWT_SIGNING_KEY`, `POSTGRES_PASSWORD` and `ADMIN_PASSWORD`, is already
gitignored, and is already sourced by `deploy.sh:142`.

```bash
# on the Unraid console, once
base64 -w0 AuthKey_ABCD123456.p8   # macOS: base64 -i AuthKey_ABCD123456.p8
```

Rejected alternative: bind-mounting the `.p8` as a read-only volume with an
`APNS_KEY_PATH`. It works, but it introduces a *second* secret-delivery
mechanism on that box with its own file permissions, its own backup story and
its own way to be missing after a container recreate. One secret store is
better than two. Base64 also sidesteps the fact that a PEM's embedded newlines
survive `set -a; . ./.env` badly.

The loader accepts **either** form — raw PEM if the value starts with
`-----BEGIN`, base64 otherwise. Costs three lines, removes a whole class of
"why is the key invalid" support.

### 1.3 Configuration surface

Add to `docker-compose.yml` under the `dailybread` service, following the
existing `NTFY_*` block's style (optional, defaulting empty):

```yaml
      # APNs (native push). All five must be set or the sender no-ops, exactly
      # like ntfy. The .p8 is base64 of the file App Store Connect issues once.
      - APNS_KEY_P8=${APNS_KEY_P8:-}
      - APNS_KEY_ID=${APNS_KEY_ID:-}
      - APNS_TEAM_ID=${APNS_TEAM_ID:-}
      - APNS_BUNDLE_ID=${APNS_BUNDLE_ID:-com.jshauns.dailybread}
      - APNS_ENVIRONMENT=${APNS_ENVIRONMENT:-production}
```

**`APNS_ENVIRONMENT` defaults to `production` and this is not a typo.**
TestFlight builds — which is how this entire family gets the app — use the
**production** APNs environment. Only a build signed with a *development*
profile (Xcode → Run on a tethered device) uses sandbox. Getting this backwards
returns `400 BadDeviceToken` for every device, which reads exactly like a
malformed-token bug and costs an afternoon.

| Value | Host |
|---|---|
| `production` | `https://api.push.apple.com` |
| `sandbox` | `https://api.sandbox.push.apple.com` |

Port 443 by default. Apple also serves 2197 for networks that block 443;
worth an `APNS_PORT` only if the tunnel ever objects, which it will not.

**No-op when unconfigured**, matching `NtfyAlertService.cs:27-35`. That keeps
dev machines silent, keeps the 221-test suite credential-free, and means this
phase can be merged and deployed before Shaun creates the key.

---

## 2. The device-token registry

### 2.1 Entity

New `Daily_Bread/Data/Models/DeviceToken.cs`. Modeled on `PushSubscription`
but **fixing the two defects that model has**: no household column, and no
notion of what kind of token it is.

```csharp
public class DeviceToken
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>
    /// Denormalized from the owner at registration. The fan-out queries filter
    /// on this column rather than joining Users, so it is structurally
    /// impossible to write an audience query that forgets the household.
    /// Re-stamped on every register call, which the client makes on every
    /// launch — so a household move corrects itself within one app open.
    /// </summary>
    public Guid? HouseholdId { get; set; }

    /// <summary>Lowercase hex, 64 chars today; Apple reserves the right to lengthen it.</summary>
    public required string Token { get; set; }

    /// <summary>APNs topic. Distinguishes the iOS app from a future Mac bundle.</summary>
    public required string BundleId { get; set; }

    /// <summary>"production" | "sandbox" — a device registered from a dev build cannot be pushed from the prod host.</summary>
    public required string Environment { get; set; }

    /// <summary>"device" today; "liveactivity-start" / "liveactivity-update" when §10 happens.</summary>
    public required string Kind { get; set; }

    public string? DeviceName { get; set; }
    public string? AppVersion { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastPushAt { get; set; }
    public int FailedAttempts { get; set; }
}
```

`Kind` costs nothing now and saves a migration when Live Activities land — push-
to-start tokens are a genuinely different token that lives in the same table.

### 2.2 EF configuration and migration

In `ApplicationDbContext.cs`, beside the `PushSubscription` block (~:441):

```csharp
builder.Entity<DeviceToken>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Token).HasMaxLength(200).IsRequired();
    entity.Property(e => e.BundleId).HasMaxLength(200).IsRequired();
    entity.Property(e => e.Environment).HasMaxLength(20).IsRequired();
    entity.Property(e => e.Kind).HasMaxLength(30).IsRequired();
    entity.Property(e => e.DeviceName).HasMaxLength(200);
    entity.Property(e => e.AppVersion).HasMaxLength(50);

    // Unique on the TOKEN ALONE — see §2.3, this is the whole point.
    entity.HasIndex(e => new { e.Token, e.Kind }).IsUnique();
    entity.HasIndex(e => e.UserId);
    entity.HasIndex(e => new { e.HouseholdId, e.IsActive });

    entity.HasOne(e => e.User)
        .WithMany()
        .HasForeignKey(e => e.UserId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Add `public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();` beside the
`PushSubscriptions` set (`ApplicationDbContext.cs:33`).

Migration, using the pinned tool from `.config/dotnet-tools.json`:

```bash
dotnet tool restore
dotnet dotnet-ef migrations add AddDeviceTokens --project Daily_Bread
```

Name follows the `AddXxx` convention of every migration in the folder. The
server runs `MigrateAsync()` at boot, so deployment is `./deploy.sh rebuild`
with no manual step — same as every prior schema change.

### 2.3 The uniqueness rule, because it is a real bug class

A device token identifies **an app install on a device**, not a user. If Victor
signs out on the family iPad and Charmaine signs in, APNs hands back the *same*
token. Keying uniqueness on `(UserId, Token)` leaves two active rows for one
physical device, and Victor keeps receiving Charmaine's approval alerts on a
device he no longer uses.

So: **unique on `Token`, and registration reassigns `UserId`.** The register
endpoint is an upsert keyed on the token; whoever registered last owns the
device. The client also `DELETE`s its token on sign-out, but that call can fail
offline — the reassign-on-register is the safety net that makes the failure
harmless.

### 2.4 Endpoint

New `Daily_Bread/Api/Controllers/DevicesController.cs`, uniform with the other
sixteen controllers:

```csharp
[ApiController]
[Route("api/v1/devices")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
```

No `Roles =` — this is self-scoped, and children need push as much as parents.

| Verb | Route | Body | Returns |
|---|---|---|---|
| `POST` | `/register` | `RegisterDeviceRequest` | `204` |
| `DELETE` | `/{token}` | — | `204` always (idempotent, never an oracle) |
| `GET` | `/` | — | this caller's own devices — Phase 6, for a "signed-in devices" settings screen |

DTOs into `Api/ApiDtos.cs` as `sealed record`s, matching `LoginRequest`'s style:

```csharp
public sealed record RegisterDeviceRequest(
    string Token,
    string Environment,          // "production" | "sandbox" — from the client's build config
    string? DeviceName = null,
    string? AppVersion = null,
    string Kind = "device");
```

Rules the controller enforces:

- **`UserId` and `HouseholdId` come from the claims, never the body.**
  `ICurrentUserContext.HouseholdId` reads the `HouseholdId` JWT claim
  (`ApiCurrentUserContext.cs:37-48`); `BundleId` comes from server config, not
  the client, so a client cannot register itself under another app's topic.
- **Validate the token**: `^[0-9a-fA-F]{64,200}$`, lowercased before storing.
  This catches the classic `String(describing: deviceToken)` bug that sends
  `<abc123 def456>`; without the check it silently poisons the registry.
- A caller with a null household is accepted and stored with a null household.
  Per-user pushes ("you got paid") still work; household fan-outs simply skip
  them. That matches the posture the API already takes elsewhere
  (`ApprovalsController.cs:41-44` returns an empty queue rather than an error).
- `DELETE` returns `204` whether or not the row existed, and only deletes rows
  the caller owns. Returning `404` would let one family member probe another's
  device tokens.

---

## 3. Household-scoped fan-out — the audit fix, and it ships first

This is a security fix that stands on its own merits. It is listed as Phase 0
because building an APNs sender on top of the current audience code would take
a deferred web-push leak and promote it to a lock-screen leak.

### 3.1 One audience helper, used by everything

New `Daily_Bread/Api/HouseholdAudience.cs` (or a method on `IHouseholdGuard`,
which already owns household reasoning):

```csharp
public interface IHouseholdAudience
{
    /// <summary>Parent/Admin users inside this household. The only sanctioned
    /// way to answer "who are the parents" — GetUsersInRoleAsync alone returns
    /// every parent in the database (audit 2026-08-04, §"Structural finding").</summary>
    Task<IReadOnlyList<string>> ParentUserIdsAsync(Guid householdId, CancellationToken ct = default);

    /// <summary>The household a user belongs to, for callers that hold only a user id.</summary>
    Task<Guid?> HouseholdOfAsync(string userId, CancellationToken ct = default);
}
```

Implementation: union of `GetUsersInRoleAsync("Parent")` and `("Admin")`,
intersected with `u.HouseholdId == householdId`.

Then:

- `PushNotificationService.SendToRoleAsync` (`:266-276`) and
  `NotifyParentsAsync` (`:278-281`) take a household and route through it.
  `SendHelpRequestNotificationAsync` (`:283-305`) gains a household parameter
  from its one caller, `TrackerService.cs:823`.
- The new APNs notifier uses the same helper. There is exactly one place that
  answers "who are the parents", and it is household-filtered.

### 3.2 SignalR groups

`ChoreHub.ParentsGroup` becomes `ChoreHub.ParentsGroupFor(Guid householdId)`
returning `$"parents:{householdId:N}"`. `OnConnectedAsync` (`ChoreHub.cs:26-42`)
reads the household from the connecting principal and joins that group.
`ChoreNotificationService.NotifyDashboardChangedAsync` (`:103-106`) and
`NotifyHelpRequestedAsync` (`:139-144`) take a household and address it. The
three `Clients.User(...)` events are already correctly targeted and do not
change.

**Verify before you key on the claim.** `HouseholdId` is on API JWTs
(`ApiTokenService.cs:219-222`) but has *not* been verified on the Blazor cookie
principal — check `ApplicationUserClaimsPrincipalFactory` first. If the claim
is absent for web circuits, every parent on the web app lands in no group and
their live updates die silently. Fall back to a `UserManager` lookup in
`OnConnectedAsync` if the claim is not there; do not assume.

`Daily_Bread.Tests/SignalRSecurityTests.cs` asserts the *current* group name at
lines 67 and 124. Those assertions change by design — update them to the
per-household name and add one that proves a second household's parent does not
receive the first household's `HelpAlert`.

### 3.3 ntfy

One topic per deployment; not fixable in code. Once APNs carries help requests
reliably (Phase 4), ntfy stops being the channel that must land and becomes a
diagnostic backstop. **Do not remove it in R5** — it is the only channel with a
proven track record on that box, and removing the working thing in the same
release that adds the new thing is how you end up with no notifications at all.
Demote it in a later pass.

---

## 4. The APNs sender

### 4.1 Hand-rolled, not a library

**Decision: write it.** Roughly 150 lines across two files.

Reasons:

- .NET 10's `SocketsHttpHandler` speaks HTTP/2 natively with connection
  multiplexing. There is nothing a library adds at the transport layer that
  `HttpClient` with `DefaultRequestVersion = HttpVersion.Version20` and
  `DefaultVersionPolicy = RequestVersionExact` does not already do.
- The provider JWT is three lines of BCL (§4.2). The token caching rules are
  two numbers.
- The error handling that matters is five reason strings. A library's
  abstraction over them is not a saving; it is an indirection between you and
  Apple's actual response.
- This csproj already carries two `<!-- Direct pin: lifts the transitive … past
  GHSA-… -->` comments. Every added dependency is another transitive graph to
  audit for a family chore app. `dotAPNS` is the only actively maintained
  option and it is a thin wrapper over exactly the code below.

Files:

```
Daily_Bread/Services/Apns/ApnsOptions.cs         — bound from APNS_* env
Daily_Bread/Services/Apns/ApnsTokenProvider.cs   — singleton, caches the provider JWT
Daily_Bread/Services/Apns/ApnsSender.cs          — typed HttpClient, one device per call
Daily_Bread/Services/Apns/IApnsNotifier.cs       — the domain-facing surface (§5)
```

Registration in `Program.cs`, beside the existing notification services (~:344):

```csharp
builder.Services.Configure<ApnsOptions>(builder.Configuration.GetSection("Apns"));
builder.Services.AddSingleton<IApnsTokenProvider, ApnsTokenProvider>();
builder.Services.AddHttpClient<IApnsSender, ApnsSender>();
builder.Services.AddScoped<IApnsNotifier, ApnsNotifier>();
```

The token provider is a **singleton** and the sender is a typed (scoped)
client. That asymmetry is deliberate: the cached JWT must outlive a request, or
the 20-minute rule below is violated on every single push.

### 4.2 Provider authentication

The `.p8` body is PKCS#8 — `ECDsa.ImportPkcs8PrivateKey` reads it directly.

```csharp
var ec = ECDsa.Create();
ec.ImportFromPem(pem);                 // or ImportPkcs8PrivateKey on the base64 body

var header  = B64Url($$"""{"alg":"ES256","kid":"{{keyId}}"}""");
var payload = B64Url($$"""{"iss":"{{teamId}}","iat":{{issuedAt}}}""");
var signature = ec.SignData(
    Encoding.ASCII.GetBytes($"{header}.{payload}"),
    HashAlgorithmName.SHA256,
    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);   // <- not the default
var jwt = $"{header}.{payload}.{B64Url(signature)}";
```

**`DSASignatureFormat.IeeeP1363FixedFieldConcatenation` is mandatory.** The
overload without it produces a DER-encoded signature, which is what .NET
defaults to and what JWS forbids. The symptom is `403 InvalidProviderToken` on
every request, with a perfectly valid key — this is the single most common
hand-rolled-APNs bug and it is worth a comment in the code.

Base64url means: standard base64, `+`→`-`, `/`→`_`, padding stripped.

### 4.3 Token caching — the 20-minute and 1-hour rules

Apple imposes both, in opposite directions:

- **A provider token must not be regenerated more often than once every 20
  minutes.** Doing so earns `429 TooManyProviderTokenUpdates` and Apple may
  throttle the connection, not just the request.
- **A provider token older than 60 minutes is rejected** with
  `403 ExpiredProviderToken`.

So the cache refreshes on a **50-minute** window — comfortably inside both, with
ten minutes of slack for a slow tick or a clock that drifts. `ApnsTokenProvider`
holds `(string Jwt, DateTimeOffset IssuedAt)` behind a `SemaphoreSlim` so a
burst of concurrent sends produces one refresh, not five.

Forced refresh (on a `403 ExpiredProviderToken`) is guarded by the same 20-minute
floor: if the cached token is younger than 20 minutes, refuse to refresh and let
the send fail. Otherwise a misconfiguration turns into a refresh storm.

`iat` is **seconds** since epoch, and it is checked against Apple's clock. If the
Unraid box's clock drifts more than a few minutes, every push fails with
`InvalidProviderToken` — worth checking `timedatectl`/NTP before debugging the
signature.

### 4.4 Request shape

```
POST https://api.push.apple.com/3/device/{deviceToken}
authorization: bearer <provider jwt>
apns-topic: com.jshauns.dailybread
apns-push-type: alert
apns-priority: 10
apns-id: <our uuid, so our logs and Apple's correlate>
apns-collapse-id: <=64 bytes, optional
apns-expiration: <unix seconds>, optional
apns-thread-id: <grouping key>, optional
```

`apns-push-type` is **required** since iOS 13 and rejected-if-wrong on watchOS.
It is `alert` for everything in this plan, `liveactivity` in §10.

**`apns-expiration: 0` does not mean "never expire" — it means "attempt once
immediately, then discard".** Omit the header for "store and retry
indefinitely". This is the second most common APNs mistake.

Payload:

```json
{
  "aps": {
    "alert": { "title": "Victor finished everything today", "body": "6 chores · $4.25 waiting for approval" },
    "sound": "default",
    "category": "DAY_COMPLETE",
    "thread-id": "child-<userId>",
    "interruption-level": "active",
    "badge": 3
  },
  "route": "approvals",
  "userId": "<subject of the notification>",
  "entityId": "142"
}
```

Everything outside `aps` is ours. `route` is the deep-link key (§8.3). Keep the
whole payload under 4 KB — nowhere near it here, but do not be tempted to embed
a dashboard snapshot.

### 4.5 Response handling

Apple returns the reason as JSON: `{"reason":"Unregistered","timestamp":…}`.

| Status / reason | Action |
|---|---|
| `200` | Stamp `LastPushAt`, reset `FailedAttempts` to 0. |
| `410 Unregistered` | **Delete the row.** The app was deleted from that device; the token is dead permanently. Apple's `timestamp` says when — if a *newer* registration exists for that token, keep it. This is the required prune. |
| `400 DeviceTokenNotForTopic` | Delete. The token belongs to a different bundle ID; it will never work. |
| `400 BadDeviceToken` | **Do not delete on the first one.** This is what a wrong `APNS_ENVIRONMENT` looks like, and pruning here would wipe the entire registry on a misconfiguration. Increment `FailedAttempts`; deactivate at 3; log the configured environment in the message so the cause is in the log line. |
| `403 ExpiredProviderToken` | Force one refresh (subject to the 20-minute floor), retry the request exactly once. Never loop. |
| `403 InvalidProviderToken` | Configuration is wrong — key/team/kid mismatch, or a DER signature (§4.2). Log at `Error`, touch **no** device rows, and set a cooldown so we stop hammering Apple with a broken key. |
| `429 TooManyProviderTokenUpdates` | We violated the 20-minute rule. Back off; treat as a bug, not a transient. |
| `429 TooManyRequests` | Per-device throttle. Back off that device only. |
| `500` / `503` | Transient. One retry with jitter, then give up. A chore approval must never wait on Apple. |

Fan-out concurrency: `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism = 4`. This family has about five devices — the cap is
about not opening five TCP connections to Apple for one approval, not about
throughput.

**Every call site is fire-and-forget**, matching the existing
`_ = _pushNotificationService.…` pattern at `TrackerService.cs:823`. An APNs
outage must never fail a chore approval or roll back a ledger transaction. All
of it inside try/catch that logs and swallows.

---

## 5. Which events fire what, and who receives it

`IApnsNotifier` is the domain-facing surface. It takes the same shape as
`IChoreNotificationService` — one method per domain event, so call sites read
as intent, not as payload construction.

**Hook them where the existing `_choreNotificationService` calls already sit**,
in the service layer. Not in controllers: `ApprovalsController.cs:69` and the
Blazor UI both route through `DashboardService.QuickApproveAsync`, so a
controller-level hook would double-notify API users and miss web-initiated
actions entirely.

| Event | Trigger site | Audience | Title / body | Category | Priority | Collapse |
|---|---|---|---|---|---|---|
| Drive awaiting approval | `DrivingLogService.CreateEntryAsync` after `SaveChangesAsync`, when `Status == PendingApproval` | parents of the child's household | "A drive is waiting for approval" / "Victor · 42 min · night" | `DRIVE_PENDING` | 10 | `drive-{entryId}` |
| Help request | `TrackerService.cs:823` (alongside the existing ntfy + web push) | parents of household | "Victor needs help" / chore + reason | `HELP_REQUEST` | 10 | `help-{choreLogId}` |
| Chore awaiting approval | `TrackerService` status change where `newStatus == Completed` and the chore is not auto-approve (`:514-548`) | parents of household | "Victor finished Dishes" / "$1.50 waiting" | `CHORE_PENDING` | 5 | `approvals-{childUserId}` |
| **Day complete** | `DailyDigestHostedService` tick — *not* a per-chore hook (§6) | parents of household | "Victor finished everything today" / "6 chores · $4.25 waiting" | `DAY_COMPLETE` | 5 | `day-{childUserId}-{date}` |
| Blessing granted | `DashboardService.cs:672`, `TrackerService.cs:941` | the child, by user id | "Dad blessed Dishes" / "+$1.50 · balance $12.75" | `BLESSING` | 10 | `blessing-{childUserId}` |
| Help responded | `TrackerService.cs:927` | the child | "Mom answered about Dishes" | `HELP_RESPONSE` | 10 | `help-{choreLogId}` |
| **You got paid** | `PayoutService.RecordPayoutAsync` after `SaveChangesAsync` | the child | "You got paid $20.00" / "Cash out · new balance $2.75" | `PAYOUT` | 10 | none |

`DrivingLogService` and `PayoutService` currently inject **no** notification
service at all. Both need a new constructor dependency, which ripples into
`DrivingLogServiceTests` and `ThresholdPayServiceTests` fixtures. Budget for it.

### 5.1 Deliberate non-events

- **Chore undone** (`TrackerService.cs:576`). A lock-screen alert that says a
  parent took something back, with no action available, is a punishment
  notification. SignalR-only, as today.
- **Every individual chore completion, uncollapsed.** Victor with severe ADHD
  can clear eight chores in a burst. Eight lock-screen alerts trains a parent
  to swipe without reading, which destroys the two alerts that actually matter.
  Hence `apns-collapse-id: approvals-{childUserId}` on `CHORE_PENDING` — one
  notification per child that *updates in place* — and hence the digest.
- **Achievements and celebrations.** The app already celebrates in-app. A push
  adds nothing while they are looking at the screen it fires from.
- **Balance adjustments** (`AdjustBalanceSheet`). A parent editing a balance
  while sitting next to the child does not need to notify the child's phone.
  Reconsider only if it becomes a source of "where did my money go".

### 5.2 Quiet hours

No `apns-priority: 5` send between **21:00 and 07:00 family-local**; hold to the
next tick. In practice only the digest can fire at a bad hour, and a parent's
phone buzzing at 23:10 because a kid finally did the dishwasher is precisely how
a family turns notifications off for good. Priority-10 events (help, drive,
payout) are not held — those are the ones you *want* at 22:00.

Family-local means `IDateProvider.Now`, which already resolves the configured
family timezone (`Services/IDateProvider.cs`). Never `DateTime.UtcNow`.

---

## 6. "Victor finished everything today"

This cannot be a per-chore hook. "Everything" is a property of the *day*, and
the last chore's completion has no idea whether more chores are scheduled later
that day, or whether one is sitting Excused.

Implement as a background sweep, modeled directly on
`Services/WeeklyReconciliationHostedService.cs` — the app's only existing
scheduler and a good one: a thin timer whose logic lives entirely in a testable
service, with per-tick try/catch so one bad day cannot crash the host.

```
Daily_Bread/Services/DailyDigestHostedService.cs   — PeriodicTimer, 15 min, 15s startup delay
Daily_Bread/Services/IDailyDigestService.cs        — all the logic, unit-testable
```

Each tick, per household, per child: if every chore scheduled for
`_dateProvider.Today` has reached a terminal state (`Completed`, `Approved`, or
`Excused`), and no digest has been sent for that child on that day, and it is
inside quiet hours' window, send `DAY_COMPLETE` to the household's parents.

**Idempotence** needs a record, or a restart re-sends. Add a small table:

```csharp
public class NotificationLedger      // (UserId, Kind, DayKey) unique
{
    public int Id { get; set; }
    public required string UserId { get; set; }   // subject, not recipient
    public required string Kind { get; set; }     // "day-complete"
    public required DateOnly DayKey { get; set; } // family-local date
    public DateTime SentAt { get; set; }
}
```

A generic once-per-day guard beats a `LastDigestDate` column on
`ApplicationUser`, because the second once-per-day notification anyone thinks of
(a morning "3 chores today" nudge, an end-of-week summary) reuses it for free.
It also makes the digest service trivially testable and restart-safe.

---

## 7. Categories and actions

### 7.1 The set

Registered on the client at launch via `UNNotificationCategory` (registering
categories requires no permission and no token — do it unconditionally in
`didFinishLaunching`).

| Category | Actions | Tap destination |
|---|---|---|
| `DRIVE_PENDING` | "Open" (foreground) | Driving log, parent mode |
| `HELP_REQUEST` | "Open" (foreground) | Approvals, scrolled to the help item |
| `CHORE_PENDING` | "Open" (foreground) | Approvals |
| `DAY_COMPLETE` | "Open" (foreground) | Approvals |
| `BLESSING` | none | Earnings |
| `PAYOUT` | none | Earnings |
| `HELP_RESPONSE` | none | Today |

`apns-thread-id: child-{userId}` on everything about a specific child, so
Notification Center groups a child's notifications together rather than
interleaving three kids.

### 7.2 No money-moving actions. This is not negotiable.

The obvious feature is an **Approve** button on the notification. Do not build
it.

R4 put a biometric wall in front of every parent surface precisely because a
child holding a parent's already-unlocked phone is the threat model. A
notification action that approves a chore — crediting real money — executes
*from the lock screen or the banner*, entirely outside `RootView`, and therefore
entirely outside that wall. It would be the single easiest bypass of the feature
that just shipped.

`UNNotificationActionOptions.authenticationRequired` does not save it: that
option requires the **device passcode**, which per R4's own written threat model
("a bright child … who has watched the device passcode typed a hundred times")
is a credential the adversary has. R4 explicitly refused the passcode as a
fallback for exactly this reason; a notification action must not reintroduce it.

So every parent action is `.foreground` — it opens the app, the gate does its
job, and the approve tap happens behind a Face ID match. One extra second, and
the security model stays one model instead of two.

### 7.3 Time-sensitive interruption level

Help requests are the one case that deserves `interruption-level:
time-sensitive` so they pierce Focus. That requires the
`com.apple.developer.usernotifications.time-sensitive` entitlement — **an
entitlements change, which needs Shaun's sign-off** under the same rule as the
R3 sandbox. Ship at `active` first; raise it as its own decision once the
channel is proven.

---

## 8. The iOS app side

### 8.1 Entitlement and capability — Shaun's lever

Push needs **no Info.plist key**. It needs the `aps-environment` entitlement,
which comes from the Push Notifications capability on the App ID.

In `apps/DailyBread/project.yml`, the app target's `entitlements.properties`
block (`:65-69`) gains:

```yaml
    entitlements:
      path: Config/DailyBread.entitlements
      properties:
        com.apple.security.application-groups:
          - group.org.dailybread.shared
        aps-environment: development
```

(`development` in the source entitlements is correct — the value is rewritten to
`production` when the archive is signed with a distribution profile. This is
normal and confuses everyone once.)

That is a **change to an entitlements file**, so it is Shaun's call, not an
agent's. Same rule that governed the R3a sandbox.

`Config/DailyBread-macOS.entitlements` is hand-written and would need the same
key for the Mac app to receive push. **Recommend iOS-only for R5.** The Mac is a
desktop a parent is already sitting in front of, macOS push under the sandbox is
its own signing exercise, and the Mac app has no widget extension or app group
to reuse. Revisit after the iOS channel is boring.

`Widgets/Info.plist` and the widget target need nothing.

### 8.2 Registration flow, in order

SwiftUI has no app delegate; use `@UIApplicationDelegateAdaptor` in
`DailyBreadApp`, inside `#if os(iOS)`.

1. **At launch, unconditionally**: set
   `UNUserNotificationCenter.current().delegate`, and register the categories.
   The delegate must be set before launch finishes or a launch-from-notification
   response is lost.
2. **Never prompt at launch.** See §8.5.
3. On authorization granted → `UIApplication.shared.registerForRemoteNotifications()`.
   (It technically works without authorization — silent push does not need it —
   but this plan sends no background push, so there is nothing to gain from a
   token the user has not agreed to be notified by.)
4. `application(_:didRegisterForRemoteNotificationsWithDeviceToken:)` →
   **hex-encode properly**:
   ```swift
   let hex = deviceToken.map { String(format: "%02x", $0) }.joined()
   ```
   Not `deviceToken.description` — on modern iOS that is `{length = 32, bytes = 0x…}`
   and it is why §2.4 validates the format server-side.
5. `POST api/v1/devices/register` via a new `APIClient` method, matching the
   file's existing style:
   ```swift
   public func registerDevice(_ body: RegisterDevice) async throws
   public func unregisterDevice(token: String) async throws
   ```
6. **Re-register on**: every successful `SessionStore.login()`, every
   `bootstrap()` that lands `.signedIn`, every successful `unlockSession()`, and
   every time iOS hands over a new token. It is a cheap upsert and it is what
   keeps `HouseholdId` and `UserId` accurate on a shared device.
7. **On `signOut()`**: `DELETE api/v1/devices/{token}` **before**
   `client.logout()` — it needs a live access token. Best-effort, never blocking
   sign-out, failure logged and ignored (the reassign-on-register in §2.3 is the
   backstop).

### 8.3 Deep linking

New in the kit: `@MainActor @Observable final class NotificationRouter` holding
`var pending: Route?`, where `Route` maps onto the existing
`MainView.Section` enum (`approvals`, `driving`, `earnings`, `activity(userId:)`,
`today`).

`userNotificationCenter(_:didReceive:withCompletionHandler:)` reads
`userInfo["route"]` and `userInfo["userId"]` and sets `router.pending`.
`MainView` observes it and sets `selection` (macOS) or the tab (iOS), then
clears it.

There is no `onOpenURL`, no URL scheme and no App Intent today. This router is
where all three land when they arrive, which is worth stating so the next
feature does not invent a second mechanism.

### 8.4 The R4 interaction, which is the part that will be got wrong

A route that arrives while the session is locked **must be held, not applied**.

- If `SessionStore.state == .locked(_)`: store the route, leave the wall up,
  replay it after `unlockSession()` succeeds. Never let a notification tap put a
  parent screen on screen behind the lock.
- If signed in but the parent gate is closed: store the route, let the gate
  prompt as normal, apply on a match, **drop it on cancel**. A held route that
  survives a cancelled prompt would re-prompt forever.
- `RootView` is the only place `state` becomes UI, so the hold/replay lives
  there, not in `MainView`.

The corollary is that a notification arriving for a locked app is still
*delivered and displayed* by iOS — the alert text is rendered by the system, not
by us. So notification **content must be safe to read on a locked screen**. Keep
amounts and first names (they are already on the lock screen via the widget);
never put a help request's free-text reason in the body — put it behind the tap.

### 8.5 Permission prompt timing

**Never at launch.** There is exactly one system prompt per install, and a
denial is recoverable only through Settings, which nobody does.

Ask in context, after the value is visible, from an explicit button:

- **Parent**: the first time Approvals is opened with something in the queue,
  show a `glassCard` row — "Get told when a chore or a drive needs you" with a
  "Turn on" button. The system prompt fires only from that button.
- **Child**: after their first blessing lands — "Get told when you're blessed or
  paid".

Follow the house conventions: `SheetKit` if it becomes a sheet, `glassCard` if
it is a row, `Color.dbAccent` for the accent, inline `Label` for any error, and
**no system alert** — the app has exactly one `.alert` in it and this must not
be the second. A "Not now" is fine here (unlike R4's gate, a declined
notification prompt costs nothing security-wise), and the row reappears
periodically until answered.

If authorization is denied, show a one-line row in Settings pointing at
`UIApplication.openSettingsURLString`. Do not re-prompt; iOS will not show it
again anyway.

### 8.6 Foreground presentation

`willPresent` returns `[]` when the app is already showing the screen the
notification points at, `[.banner, .sound]` otherwise. The router knows the
current section, so this is a two-line check. Suppressing everything in the
foreground is the lazy option and it is wrong — a parent on the Planner screen
should still see that a drive came in.

---

## 9. How this interacts with SignalR

**The rule: APNs is for when the app is not in front. SignalR and the 30-second
poll are for when it is.**

The overlap is resolved **on the client**, in `willPresent` (§8.6) — never on
the server. Do not try to suppress a push server-side based on "is this user
currently connected": connection state is a race, and the failure mode is a
silently lost notification, which is worse than a duplicate banner.

**Should the native app join `/chorehub`? No — not in R5.**

Two real blockers exist: `ChoreHub`'s bare `[Authorize]` resolves to the Identity
*cookie* scheme, not Bearer (`ChoreHub.cs:11` vs. every API controller's explicit
`AuthenticationSchemes`), and the JwtBearer registration (`Program.cs:141-156`)
has no `Events.OnMessageReceived` reading `access_token` from the query string —
which SignalR's WebSocket and SSE transports require, since they cannot set an
`Authorization` header. Both are fixable in about twenty lines.

But the payoff is small. Adding `SignalR-Client-Swift` to a client whose entire
third-party surface is *one* package (Yams) buys a 30-second latency improvement
over a poll that already works, on a screen the user is already looking at. Push
closes the actual gap, which is "the app is closed". Revisit if Live Activities
need sub-second updates.

What *does* change in R5 is §3.2 — the hub's group keying gets fixed regardless,
because the leak is a leak whether or not a native client ever connects.

One optional follow-on (Phase 6): once push is proven, the iOS `.poll` interval
can relax from 30 s to 60 s. The urgent cases now arrive out-of-band, and the
poll exists for freshness, not for alerting. Battery win, no felt regression.

---

## 10. Live Activities — later, and here is the sketch

A `ChoreDayActivity` on the Lock Screen and Dynamic Island: progress through
today's chores (4 / 7) and money earned so far, started when the first chore of
the day is checked and ended at day rollover.

What it needs, so the groundwork above does not have to be redone:

- **The target already exists.** Live Activities live in a widget extension, and
  `DailyBreadWidgets` is already there (iOS-only, `project.yml:135-161`).
- `NSSupportsLiveActivities: true` in the **app's** Info.plist — via
  `project.yml` `targets.DailyBread.info.properties`, the same mechanism that
  carried `NSFaceIDUsageDescription`. Not the widget's plist.
- **Different tokens.** `Activity.pushToStartToken` (one per app install, for
  starting an activity from the server) and `activity.pushToken` (one per live
  activity, for updating it) are both distinct from the device token and both
  arrive asynchronously. They go in the same `DeviceToken` table under
  `Kind = "liveactivity-start"` / `"liveactivity-update"` — which is exactly why
  §2.1 has a `Kind` column now.
- Topic is `com.jshauns.dailybread.push-type.liveactivity`,
  `apns-push-type: liveactivity`, `apns-priority: 10`, payload
  `{"aps":{"timestamp":…,"event":"update","content-state":{…}}}` where
  `content-state` matches the `ActivityAttributes.ContentState` shape exactly.
- Deferred deliberately: highest effort, lowest necessity. The Home Screen
  widget already does the "glance at progress" job, and a Live Activity that
  updates from the server needs the whole push pipeline to be boring first.

---

## 11. Tests

The .NET suite is at **221 passing** (measured 2026-08-06; the figures in
`screentime_ui/README.md:87` and `START-HERE.md:268` are stale). Keep it green.

| File | Covers |
|---|---|
| `ApnsJwtTests` | Sign with a throwaway P-256 key: three segments; **signature is exactly 64 bytes** (P1363, not DER); header `alg`/`kid`; payload `iss`/`iat`. |
| `ApnsTokenProviderTests` | Fake clock: two calls inside 20 min return the same string; a call after 50 min returns a new one; a forced refresh inside 20 min is refused. |
| `ApnsSenderTests` | Stub `HttpMessageHandler`: `410` deletes the row; `400 BadDeviceToken` increments and does **not** delete; `403 ExpiredProviderToken` triggers exactly one refresh and one retry; `403 InvalidProviderToken` touches no rows. |
| `DeviceRegistryTests` | Token reassignment across users on the same device; hex validation rejects `<abc 123>`; household stamped from the claim, never the body. |
| `HouseholdAudienceTests` | **Seed a second household; assert zero cross-household recipients** for `ParentUserIdsAsync`, for `SendToRoleAsync`, and for the APNs fan-out. This is the audit finding's regression guard and it ships before any sender does. |
| `SignalRSecurityTests` | Update the two group assertions (`:67`, `:124`) to the per-household name; add a cross-household `HelpAlert` isolation case. |
| `DailyDigestServiceTests` | Fires once when all of today's chores are terminal; does not fire twice; respects quiet hours; uses family-local date, not UTC. |

**Client, with no APNs and no `.p8`:**

```bash
xcrun simctl push <udid> com.jshauns.dailybread payload.json
```

This exercises the entire client path — categories, delegate, router,
gate hold/replay, foreground suppression — with no key, no server change and no
physical device. **Build the whole client slice against it first.** That is what
makes Phase 1 independent of Shaun's App Store Connect clicks.

Simulators are in `apps/BreadBox/BreadBox/CommandCenter.swift:35-36` (kid
`C3D85F83-…`, parent `B2BA5196-…`). Do not invoke BreadBox tasks from an agent
session — they are hard-wired to Shaun's own checkout.

---

## 12. Phased implementation order

### Phase 0 — Household scoping (ships alone, needs nothing from Apple)

`*.p8` into `.gitignore`. `IHouseholdAudience`. Filter
`PushNotificationService.SendToRoleAsync` / `NotifyParentsAsync` /
`SendHelpRequestNotificationAsync`. Per-household SignalR groups, after
verifying the `HouseholdId` claim exists on the Blazor cookie principal. Update
`SignalRSecurityTests`.

*Why first:* it is a security fix that is valuable with or without R5, and it is
the foundation every sender below stands on. Merging APNs before it means
writing the leak twice.

### Phase 1 — The client, driven by `simctl push` (no server change)

App delegate adaptor, category registration, `UNUserNotificationCenterDelegate`,
`NotificationRouter`, the R4-aware hold/replay in `RootView`, foreground
suppression, and the in-context permission ask. Plus the `aps-environment`
entitlement and the App ID capability — **the one item that needs Shaun**.

*Verifiable:* hand-write a payload, `xcrun simctl push`, watch it land on the
right screen behind the right gate.

### Phase 2 — The registry (still sends nothing)

`DeviceToken` entity, EF config, `AddDeviceTokens` migration,
`DevicesController`, `APIClient.registerDevice`/`unregisterDevice`, and the
`SessionStore` hooks on login / bootstrap / unlock / signOut.

*Verifiable:* a row appears with the right `UserId` and `HouseholdId` on login;
it moves to the other user when the other user signs in on the same device; it
disappears on sign-out.

### Phase 3 — **Smallest first shippable slice: one event, end to end**

`ApnsOptions`, `ApnsTokenProvider`, `ApnsSender`, `IApnsNotifier`, the `APNS_*`
compose block, and exactly **one** wired event: **"a drive is waiting for your
approval"**.

*Why that one, and not help requests:* help requests already have two working
channels (ntfy + web push), so wiring them first proves nothing new and risks
triple-notifying the family while the pipeline is unproven. Drives have **zero**
notifications today, the trigger is a single unambiguous line after
`SaveChangesAsync` in `DrivingLogService.CreateEntryAsync`, the audience is
exactly "parents of this household" — which is the fan-out path that most needs
proving — and Victor can generate one in two taps for a live test.

That one event proves the `.p8`, the environment, the topic, the JWT signature
format, the household fan-out, the 410 prune, and the deep link. Everything
after it is repetition.

### Phase 4 — The rest of the events

`CHORE_PENDING` with its collapse-id. Help request moved onto APNs (keeping
ntfy). `BLESSING`, `HELP_RESPONSE`, `PAYOUT` to the child.
`DrivingLogService` and `PayoutService` gain their notifier dependency; their
test fixtures follow.

### Phase 5 — "Victor finished everything today"

`NotificationLedger` + migration, `IDailyDigestService`,
`DailyDigestHostedService`, quiet hours.

*Why last:* it is the only piece that needs new day-state logic and a scheduler,
and it is the one that is visibly wrong if the timezone is wrong. Everything
before it is proven by then, so a bug here is unambiguously a digest bug.

### Phase 6 — Optional follow-ons

Badge counts (`aps.badge` = the recipient's pending-approval count; needs a
per-recipient payload, so it is not free). `GET api/v1/devices` and a "signed-in
devices" settings screen. Relaxing the iOS poll to 60 s. Time-sensitive
interruption level (entitlement — Shaun). macOS push. Live Activities (§10).

---

## 13. What only Shaun can do

Appending to `GETTING_REAL_2026-08-03.md`'s list:

1. **Create the APNs key** in App Store Connect (Users and Access → Integrations
   → Keys), restricted to `com.jshauns.dailybread`. Download the `.p8` — one
   chance only — and note the Key ID.
2. **Enable the Push Notifications capability** on the `com.jshauns.dailybread`
   App ID in Certificates, Identifiers & Profiles. Without it the entitlement
   does not provision and Xcode Cloud fails at signing.
3. **Sign off on adding `aps-environment`** to `Config/DailyBread.entitlements`
   via `project.yml`. Entitlements are his lever, per the R3a precedent.
4. **Put the `APNS_*` values into `/mnt/user/appdata/Daily_Bread/.env`** on
   Unraid and run `./deploy.sh rebuild`.
5. **Accept the notification prompt** on each family device, once each.

Unrelated debt worth noticing while in this area: `PUBLIC_BASE_URL`, read by
`NtfyAlertService.cs:54` for the click-through target, is absent from
`docker-compose.yml`, the appsettings templates and `deploy.sh` — so production
ntfy alerts already ship without a tappable link. APNs routes are internal and
do not need it, so this is not R5's to fix, but it is one line if anyone is
already in the compose file.
