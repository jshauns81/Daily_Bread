# Daily Bread — Native App (iOS / macOS)

The SwiftUI client for a self-hosted Daily Bread server. One codebase,
runs on iPhone, iPad, and Mac. Design language: **Graphite & Glass** —
system surfaces, color only where it means something (accent = interactive,
gold = money, red = Help).

## First build (about 5 minutes)

1. **One-time tool install** (Terminal):

   ```
   brew install xcodegen
   ```

2. **Generate the Xcode project**:

   ```
   cd apps/DailyBread
   xcodegen
   ```

   This creates `DailyBread.xcodeproj` from `project.yml`.
   (The .xcodeproj is generated output — don't commit it.)

3. **Open and run**:

   ```
   open DailyBread.xcodeproj
   ```

   - Pick a destination at the top: an iPhone simulator, or **My Mac**.
   - Press **Run** (⌘R).

4. **Signing** (only needed for a real iPhone):
   Target **DailyBread** → *Signing & Capabilities* → check
   *Automatically manage signing* → pick your **Personal Team**.
   Free-team installs on a real phone expire after 7 days — rebuild to renew.
   Simulator and Mac need nothing.

5. **First launch** — the app asks for your server:
   - Simulator or Mac, with the dev server running locally: `http://localhost:5000`
   - A real phone on your network: `http://<your-Macs-LAN-IP>:5000` or your public HTTPS URL

   Then sign in with any account from the web app (dev seed: the admin
   account works; a Child account shows the kid experience).

## What's here

```
project.yml                  XcodeGen definition (source of truth for the project)
Packages/DailyBreadKit/      Shared logic - no UI assumptions
  Core/                      Money (decimal-string wire type), DayDate, LenientDate
  Models/                    Mirrors of the /api/v1 DTOs
  Networking/                APIClient actor - auto token refresh on 401
  Auth/                      Keychain storage + SessionStore (app auth state)
  Design/                    Graphite & Glass: themes-as-accents, invariants, haptics
  Tests/                     Wire-convention tests (run with `swift test`)
DailyBread/                  The app
  App/                       Entry point, root routing, server setup, login, shell
  Features/Today/            Kid's chore list - optimistic toggle, Help sheet
  Features/Earnings/         Balance hero, goal progress, history
  Features/Approvals/        Parent queue - gold approve + help responses
  Features/Home/             Parent dashboard
  Features/Settings/         Theme picker, server, sign out
```

## Design rules (do not break)

- Themes change the **accent only**. Surfaces are system materials, always.
- **Gold** appears on money and the Approve moment. Nowhere else.
- **Red** appears on Help and errors. Nowhere else.
- Neutrals/materials live in `DesignSystem.swift` — tune them there after
  looking at real devices, never inline in views.

## Kit tests

```
cd Packages/DailyBreadKit
swift test
```
