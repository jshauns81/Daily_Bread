# Daily Bread — Auth Migration Plan

> Status: **DRAFT for review** · Created 2026-06-19
> Owner: maintainer

## Goal

- **Parent accounts:** Authentik SSO, MFA-capable.
- **Child account:** local username + password that **stays signed in** on the installed PWA. No PIN.
- **One consistent cookie** for everyone (see "cookie-alignment decision").
- **Delete PIN / Kid Mode entirely.**

Sequencing: backend auth first, **site/UI refresh as a separate effort afterward** (the primary desktop experience gets top billing there).

---

## The cookie-alignment decision (the crux)

Authentik is wired as an **external login provider feeding ASP.NET Identity** — *not* a parallel auth scheme.

Parents redirect to Authentik → callback → we find/provision their local `ApplicationUser`
(linked in `AspNetUserLogins`) → `SignInManager` issues **the same `.DailyBread.Auth` cookie
a child gets from the password form.**

Result: one cookie type, one config, uniform `[Authorize]` / roles / sign-out. The only
difference between users is *how they obtained* the cookie, not the cookie itself.

---

## Phase 0 — Prerequisites *(low-risk, ship immediately)*

**Persist DataProtection keys — now mandatory, not optional.**
OIDC nonce/correlation cookies *and* the persistent auth cookie are encrypted with these keys.
Today they regenerate on every redeploy (the startup warning we saw), which logs everyone out
and would break OIDC redirects mid-flow.

- [ ] Mount a volume for `/root/.aspnet/DataProtection-Keys`
      (e.g. host `appdata/dailybread-keys` → container path).
- [ ] In `Program.cs`: `PersistKeysToFileSystem(...)` + `SetApplicationName("DailyBread")`.
- [ ] Add the same volume to `docker-compose.yml` so `deploy.sh` keeps it.

## Phase 1 — Session longevity & cookie hardening *(serves "don't sign in every time")*

In `ConfigureApplicationCookie`:

- [ ] `ExpireTimeSpan` **7d → 60d** (or 90), keep `SlidingExpiration = true`.
      Every visit renews; near-daily use = effectively permanent login.
- [ ] Always issue the child cookie with **`isPersistent: true`** (not a session cookie).
- [ ] `SecurePolicy` `SameAsRequest` → **`Always`** (always HTTPS via Cloudflare).
- [ ] Keep `HttpOnly = true`, `SameSite = Lax`, host-only domain, `Path = /`.

**iOS-PWA durability notes (honest caveats — the PWA surface is the fragile one):**

- A **server-set, HttpOnly, first-party** cookie is **not** subject to Safari ITP's 7-day cap
  (that cap only hits JS `document.cookie`-set cookies). Our server `Set-Cookie` persistent
  cookie is the durable path — already server-set. ✅
- ITP still clears first-party data after **7 consecutive days of zero interaction** —
  non-issue for daily use.
- Worst case (iOS evicts PWA storage under memory pressure) = **one** re-login. Can't reach
  zero on the platform.
- *Future nicety:* `BiometricAuthService` + `biometric-auth.js` (WebAuthn) already exist —
  Face ID re-login instead of typing is a clean follow-up. Out of scope, flagged.

## Phase 2 — Authentik OIDC for parents

**Authentik side** — this is the **first real per-app integration** on the Authentik instance
(Phase 1 stack up on `:9000`; the **pending Cloudflare wiring is a hard dependency — close it first**):

- [ ] OIDC Provider (auth-code flow + PKCE) + Application "Daily Bread".
- [ ] Redirect URI: `https://<your-domain>/signin-oidc`.
- [ ] Group `dailybread-parents`; include groups in the token.
- [ ] Map an admin parent → Parent + Admin, other parent → Parent.

**App side:**

- [ ] `.AddOpenIdConnect("Authentik", …)` — Authority = Authentik issuer URL,
      ClientId/Secret from **env (never committed)**, `CallbackPath = /signin-oidc`,
      scopes `openid profile email groups`, `SaveTokens`.
- [ ] Blazor Server sharp edge: challenge fires from a plain `GET /Account/ExternalLogin`
      endpoint (not inside the interactive circuit); callback → provision/link →
      `SignInAsync(isPersistent: true)`.
- [ ] Group claim → role mapping.
- [ ] `Login.razor`: add "Sign in with Authentik" button beside the username/password form
      (children use the form, parents use the button).
- [ ] **Break-glass:** keep local-password capability for at least your admin account, so an
      Authentik outage can't lock anyone out of the app.
      **Recommend SSO-preferred, local-fallback — not SSO-only.**

OIDC correlation/nonce cookies are short-lived and separate from the app cookie; behind
Cloudflare ensure forwarded proto is honored (already `UseForwardedHeaders`). Optional
hardening: scope `KnownProxies`/`KnownNetworks` to Cloudflare instead of `Clear()`.

## Phase 3 — Remove PIN / Kid Mode *(after Phase 2 is proven)*

Concrete removal list (from the code reference map):

- [ ] **Delete:** `Components/Pages/KidMode/KidLogin.razor` (+ `.css`);
      `Services/KidModeService.cs` + `IKidModeService`.
- [ ] **`Program.cs`:** drop `MapPost("/auth/pin")` + `PinLoginRequest`;
      remove `/kid` from the anonymous path allowlist; remove the `IKidModeService` registration.
- [ ] **`IAuthenticationService.cs`:** remove `PinCredential`, the `_kidModeService`
      dependency + ctor param, the `PinCredential` switch branch, and `SignInWithPinAsync`.
- [ ] **`ChildProfile.cs`:** drop `PinHash` (keep the model — used for DisplayName / ledger /
      achievements). **EF migration:** `DropColumn PinHash` from `ChildProfiles`.
- [ ] **`DevDataSeeder.cs`:** remove `HashPin` + PIN seeding.
- [ ] **`Admin/Users.razor`:** replace PIN set/clear UI + "/kid – no login required" copy
      with a normal "set / reset password" flow for the child.
- [ ] **`NavMenu.razor` / `Login.razor`:** remove the `/kid` kid-mode links.
- [ ] **Child onboarding:** give the existing `ApplicationUser` a username + initial password,
      keep the Child role. The child logs in at `/Account/Login` → lands on the **existing** Child
      dashboard on `/Home` (already role-based, no rebuild needed).

## Phase 4 — Verify (on the real devices)

- [ ] **Child:** install PWA → log in once → force-quit → reopen next day = still signed in;
      redeploy container → still signed in (keys persisted). Test on the actual device.
- [ ] **Parent:** desktop → Authentik redirect → back as Parent, MFA enforced at Authentik.
- [ ] **Admin:** SSO works **and** local break-glass works.
- [ ] `curl /auth/pin` → 404; no `/kid` route resolves.
- [ ] Auth + antiforgery cookies survive a redeploy.

---

## Decisions needed

1. **SSO-only vs local break-glass** for admins — recommend **break-glass**.
2. **Session length** — 60 or 90 days?
3. **Parent provisioning** — auto-create by matching email on first SSO login, vs pre-create
   the two accounts + link?
4. Confirm **Cloudflare / Authentik wiring** is finished first (Phase 2 hard dependency).

## Notes / context

- Stack: .NET 9 Blazor Server + EF Core + PostgreSQL, behind Cloudflare tunnel
  (`<your-domain>`). Container `dailybread-app`, compose project `daily_bread`.
- The Child dashboard (`/Home`) is already built and role-gated — removing Kid Mode just
  reroutes the child through the normal login form into the same dashboard.
- Phases 0–1 are safe and independently shippable now, and they fix the logout-on-redeploy
  issue regardless of the OIDC timeline.
