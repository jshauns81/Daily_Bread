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
            title: "Pull + regen project",
            script: """
            cd \(Config.checkout)
            git pull --rebase --autostash
            cd apps/DailyBread && xcodegen generate
            echo "Project regenerated — reopen Xcode-beta if it was open."
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
