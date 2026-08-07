# 🍞 BreadBox

The one-window admin panel for the Daily Bread workflow: every recurring
incantation from the project's life as a button, so nothing has to be
re-found in scrollback again.

## Install

```
apps/BreadBox/install.sh
```

Generates the project, builds Release, and puts a real copy in
`/Applications` — then open it once and pin it to the Dock. Run the same
command after any change to BreadBox; it quits a running copy first, so
the only manual step is relaunching.

Launching from Xcode works too, but leaves the app in DerivedData where a
Clean Build Folder deletes it out from under the Dock icon. Pass a
different destination as the first argument (`install.sh ~/Applications`)
if you would rather keep it out of `/Applications`.

BreadBox is macOS-only and unsandboxed on purpose — its whole job is
running git, docker, dotnet and xcodebuild on your behalf through a login
shell, so everything resolves exactly as it does in Ghostty.

## What the buttons do

- **Git** — status, `pull --rebase --autostash` + regen of BOTH generated
  projects, DailyBread's and BreadBox's own (the 2026-08-05 icon taught us
  that a pulled project.yml change is invisible until its project is
  regenerated), push.
- **Dev server** — start the LAN profile in the background (logs to
  `~/Library/Logs/DailyBread-server.log`), stop it, tail the log. Secrets
  come from the repo's `.env`, parsed at launch and never printed.
- **Dev database** — timestamped `pg_dump` backups into `~/DailyBreadBackups`
  (revealed in Finder), apply EF migrations, and a confirm-gated **local
  dev reset** that backs up first, drops and recreates the dev database,
  migrates, and lets the seeder repopulate on next server start. It cannot
  reach the production database.
- **Production** — the family's live server at `dailybread.simmserv.org`
  (Unraid, over the `unraid` SSH alias). **Status** prints the deployed
  commit next to local `origin/master`, the containers, health, and the
  newest applied migration — the four things worth knowing before shipping.
  **Deploy master…** confirms first, then pulls and runs the box's own
  `deploy.sh rebuild` (which takes its own pre-migrate backup, keeping 20)
  and polls health until the family is on the new build. **Back up prod DB**
  streams a `pg_dump` straight into `~/DailyBreadBackups`. **Tail prod log**
  is `docker logs` on the app container. Deploy is the only write.
- **Checks** — run the screen walker on either simulator (screenshots land
  in `~/Desktop/DailyBread Walks` and open automatically) and a both-platform
  build check.

The header shows live status: dev server health, the dev Postgres container,
**Live** (production health through the tunnel), and the checkout's branch
position. One task runs at a time, streaming into the log pane — an admin
panel that lets "reset the database" race "back up the database" is a trap,
not a tool.

Production buttons need the `unraid` host in `~/.ssh/config` with key auth;
that is the only setup they assume.

## Configuration

Paths, simulator UDIDs, and the Xcode-beta requirement (stable actool
crashes on the app's Icon Composer icon) live in `Config` at the top of
`BreadBox/CommandCenter.swift`. This is a personal tool for a personal
machine: configuration is source, not settings UI.
