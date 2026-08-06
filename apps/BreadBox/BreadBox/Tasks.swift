import Foundation

/// The button catalog — every recurring incantation from the project's life,
/// written down once. Scripts assume Config paths and the login-shell PATH.
enum Catalog {
    static let git: [TaskSpec] = [
        TaskSpec(
            id: "git-status",
            title: "Status",
            script: """
            cd \(Config.checkout)
            git status -sb
            echo "---"
            git log --oneline -5
            """),
        TaskSpec(
            id: "git-pull",
            title: "Pull + regen projects",
            script: """
            cd \(Config.checkout)
            git pull --rebase --autostash
            cd apps/DailyBread && xcodegen generate
            cd ../BreadBox && xcodegen generate
            echo "Both projects regenerated — reopen Xcode if it was open."
            """),
        TaskSpec(
            id: "git-push",
            title: "Push my commits",
            script: "cd \(Config.checkout) && git push"),
    ]

    static let server: [TaskSpec] = [
        TaskSpec(
            id: "server-start",
            title: "Start server (LAN)",
            script: """
            if lsof -ti :5100 >/dev/null; then
              echo "Already running on :5100."
            else
              cd \(Config.checkout)
              nohup dotnet run --project Daily_Bread --launch-profile lan \
                >> "\(Config.serverLog)" 2>&1 &
              echo "Starting… log: \(Config.serverLog)"
              for i in {1..30}; do
                sleep 1
                curl -s -o /dev/null http://127.0.0.1:5100/api/v1/health && { echo "Healthy."; exit 0; }
              done
              echo "Not healthy after 30s — check the log."
              exit 1
            fi
            """),
        TaskSpec(
            id: "server-stop",
            title: "Stop server",
            script: "lsof -ti :5100 | xargs kill 2>/dev/null && echo Stopped. || echo Nothing on :5100."),
        TaskSpec(
            id: "server-log",
            title: "Tail server log",
            script: "tail -40 \"\(Config.serverLog)\" 2>/dev/null || echo 'No log yet.'"),
    ]

    static let database: [TaskSpec] = [
        TaskSpec(
            id: "db-backup",
            title: "Back up now",
            script: """
            mkdir -p "\(Config.backups)"
            STAMP=$(date +%Y%m%d-%H%M%S)
            OUT="\(Config.backups)/dailybread-$STAMP.sql.gz"
            docker exec \(Config.dbContainer) pg_dump -U dailybread dailybread | gzip > "$OUT"
            echo "Backed up: $OUT ($(du -h "$OUT" | cut -f1))"
            """,
            revealOnSuccess: Config.backups),
        TaskSpec(
            id: "db-migrate",
            title: "Apply migrations",
            script: "cd \(Config.checkout) && dotnet ef database update --project Daily_Bread"),
        TaskSpec(
            id: "db-reset",
            title: "Reset dev data…",
            script: """
            cd \(Config.checkout)
            echo "Backing up first…"
            mkdir -p "\(Config.backups)"
            docker exec \(Config.dbContainer) pg_dump -U dailybread dailybread \
              | gzip > "\(Config.backups)/pre-reset-$(date +%Y%m%d-%H%M%S).sql.gz"
            lsof -ti :5100 | xargs kill 2>/dev/null || true
            docker exec \(Config.dbContainer) psql -U dailybread -d postgres \
              -c 'DROP DATABASE dailybread WITH (FORCE); CREATE DATABASE dailybread;'
            dotnet ef database update --project Daily_Bread
            echo "Fresh and migrated. Start the server to reseed the test family."
            """,
            confirm: "This wipes the LOCAL dev database (users, chores, history) and rebuilds it empty. A backup is taken first, and the seeder repopulates test data on the next server start. The Unraid/production database is untouched."),
    ]

    /// The family's live server. Everything here talks to Unraid over SSH;
    /// the one write is the deploy, and it confirms first.
    static let production: [TaskSpec] = [
        TaskSpec(
            id: "prod-status",
            title: "Status",
            script: """
            echo "— deployed:"
            ssh \(Config.prodHost) 'cd \(Config.prodDir) && git log --oneline -1'
            echo "— local origin/master:"
            cd \(Config.checkout) && git fetch origin -q 2>/dev/null; git log --oneline -1 origin/master
            echo "— containers:"
            ssh \(Config.prodHost) 'docker ps --format "{{.Names}} {{.Status}}" | grep dailybread'
            echo "— health:"
            curl -s -m 8 \(Config.prodHealthURL.absoluteString); echo
            echo "— web boot (the 2026-08-06 outage was invisible to /health):"
            JS=$(curl -s -m 8 https://dailybread.simmserv.org/ | grep -oE 'src="[^"]*blazor[.]web[^"]*"' | head -1 | cut -d'"' -f2)
            if [ -z "$JS" ]; then echo "  no blazor script tag in the page — web app is NOT rendering"; else
              case "$JS" in /*) ;; *) JS="/$JS";; esac
              CODE=$(curl -s -m 8 -o /dev/null -w "%{http_code}" "https://dailybread.simmserv.org$JS")
              echo "  $JS → HTTP $CODE (200 = web app boots; 302 = the black-screen failure)"
            fi
            echo "— schema:"
            ssh \(Config.prodHost) 'docker exec \(Config.prodDbContainer) psql -U dailybread -d dailybread -t -A \
              -c "SELECT \\"MigrationId\\" FROM \\"__EFMigrationsHistory\\" ORDER BY 1 DESC LIMIT 1;"'
            """),
        TaskSpec(
            id: "prod-deploy",
            title: "Deploy master…",
            script: """
            set -e
            ssh \(Config.prodHost) 'cd \(Config.prodDir) && git pull origin master && ./deploy.sh rebuild --verbose'
            echo "— waiting for health…"
            for i in $(seq 1 40); do
              sleep 3
              if [ "$(curl -s -m 5 \(Config.prodHealthURL.absoluteString) || true)" = "Healthy" ]; then
                echo "Healthy — checking the web app boots too (health alone hid the 2026-08-06 outage)…"
                JS=$(curl -s -m 8 https://dailybread.simmserv.org/ | grep -oE 'src="[^"]*blazor[.]web[^"]*"' | head -1 | cut -d'"' -f2)
                case "$JS" in "");; /*);; *) JS="/$JS";; esac
                CODE=$(curl -s -m 8 -o /dev/null -w "%{http_code}" "https://dailybread.simmserv.org$JS")
                if [ -n "$JS" ] && [ "$CODE" = "200" ]; then
                  echo "Web app boots ($JS → 200) — the family is on the new build."
                else
                  echo "API is Healthy but the WEB APP IS NOT BOOTING (script: ${JS:-none} → HTTP ${CODE:-n/a})."
                  exit 1
                fi
                exit 0
              fi
            done
            echo "Not healthy after 2 minutes — read the deploy output above."
            exit 1
            """,
            confirm: """
            Deploys origin/master to the family's live server at \
            dailybread.simmserv.org. The app is down for the couple of minutes \
            it takes to rebuild the container, and migrations apply on boot. \
            deploy.sh takes its own database backup first (db-backups/ on the \
            box, 20 kept). Nobody should be mid-chore.
            """),
        TaskSpec(
            id: "prod-backup",
            title: "Back up prod DB",
            script: """
            mkdir -p "\(Config.backups)"
            STAMP=$(date +%Y%m%d-%H%M%S)
            OUT="\(Config.backups)/prod-$STAMP.sql.gz"
            ssh \(Config.prodHost) 'docker exec \(Config.prodDbContainer) pg_dump -U dailybread dailybread' \
              | gzip > "$OUT"
            echo "Backed up: $OUT ($(du -h "$OUT" | cut -f1))"
            """,
            revealOnSuccess: Config.backups),
        TaskSpec(
            id: "prod-logs",
            title: "Tail prod log",
            script: "ssh \(Config.prodHost) 'docker logs --tail 40 \(Config.prodAppContainer) 2>&1'"),
    ]

    static let checks: [TaskSpec] = [
        TaskSpec(
            id: "walk-kid",
            title: "Screen walk · kid",
            script: walker(sim: Config.kidSim, label: "kid")),
        TaskSpec(
            id: "walk-parent",
            title: "Screen walk · parent",
            script: walker(sim: Config.parentSim, label: "parent")),
        TaskSpec(
            id: "build-check",
            title: "Build check (both)",
            script: """
            cd \(Config.checkout)/apps/DailyBread
            xcodegen generate
            SCRATCH=$(mktemp -d)
            xcodebuild -project DailyBread.xcodeproj -scheme DailyBread \
              -destination 'generic/platform=iOS Simulator' \
              -derivedDataPath "$SCRATCH/ios" build 2>&1 | grep -E "BUILD|error:" | tail -2
            xcodebuild -project DailyBread.xcodeproj -scheme DailyBread \
              -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO \
              -derivedDataPath "$SCRATCH/mac" build 2>&1 | grep -E "BUILD|error:" | tail -2
            rm -rf "$SCRATCH"
            """),
    ]

    /// The automated screenshot tour (docs/UI_SWEEP_2026-08-03.md) — runs the
    /// walker on one simulator and opens the exported screenshots.
    private static func walker(sim: String, label: String) -> String {
        """
        cd \(Config.checkout)/apps/DailyBread
        xcodegen generate
        STAMP=$(date +%Y%m%d-%H%M%S)
        OUT="\(Config.walks)/$STAMP-\(label)"
        SCRATCH=$(mktemp -d)
        xcrun simctl boot \(sim) 2>/dev/null || true
        xcodebuild test -project DailyBread.xcodeproj -scheme DailyBread \
          -destination 'platform=iOS Simulator,id=\(sim)' \
          -derivedDataPath "$SCRATCH/dd" \
          -only-testing:DailyBreadUITests/ScreenWalkerTests/testWalkEveryScreen \
          -resultBundlePath "$SCRATCH/walk.xcresult" 2>&1 | grep -E "Test Case|error:" | tail -4
        mkdir -p "$OUT"
        xcrun xcresulttool export attachments --path "$SCRATCH/walk.xcresult" --output-path "$OUT"
        rm -rf "$SCRATCH"
        echo "Screenshots: $OUT"
        open "$OUT"
        """
    }
}
