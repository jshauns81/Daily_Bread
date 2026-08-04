# 🍞 BreadBox

The one-window admin panel for the Daily Bread workflow: every recurring
incantation from the project's life as a button, so nothing has to be
re-found in scrollback again.

## First run

```
cd apps/BreadBox
xcodegen generate
open BreadBox.xcodeproj
```

Run it once from Xcode, then keep it in the Dock (Product → Show Build
Folder in Finder → drag BreadBox.app wherever you like). It is macOS-only,
unsandboxed on purpose — its whole job is running git, docker, dotnet and
xcodebuild on your behalf through a login shell, so everything resolves
exactly as it does in Ghostty.

## What the buttons do

- **Git** — status, `pull --rebase --autostash` + project regen (the safe
  dance for this repo's rebase-pull config), push.
- **Server** — start the LAN profile in the background (logs to
  `~/Library/Logs/DailyBread-server.log`), stop it, tail the log. Secrets
  come from the repo's `.env`, parsed at launch and never printed.
- **Database** — timestamped `pg_dump` backups into `~/DailyBreadBackups`
  (revealed in Finder), apply EF migrations, and a confirm-gated **local
  dev reset** that backs up first, drops and recreates the dev database,
  migrates, and lets the seeder repopulate on next server start. It cannot
  reach the Unraid/production database.
- **Checks** — run the screen walker on either simulator (screenshots land
  in `~/Desktop/DailyBread Walks` and open automatically) and a both-platform
  build check.

The header shows live status: server health, the Postgres container, and
the checkout's branch position. One task runs at a time, streaming into the
log pane — an admin panel that lets "reset the database" race "back up the
database" is a trap, not a tool.

## Configuration

Paths, simulator UDIDs, and the Xcode-beta requirement (stable actool
crashes on the app's Icon Composer icon) live in `Config` at the top of
`BreadBox/CommandCenter.swift`. This is a personal tool for a personal
machine: configuration is source, not settings UI.
