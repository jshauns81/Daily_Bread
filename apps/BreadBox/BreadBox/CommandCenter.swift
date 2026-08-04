import AppKit
import Foundation
import Observation

/// One button's worth of work: a shell script run through a login shell so
/// Homebrew tools (docker, dotnet, xcodegen) resolve exactly as they do in
/// Ghostty. Destructive specs carry a confirmation string and never run
/// without the alert.
struct TaskSpec: Identifiable, Hashable {
    let id: String
    let title: String
    let script: String
    /// Non-nil makes the button confirm first — the text is the alert body.
    var confirm: String? = nil
    /// Reveal this path in Finder when the task succeeds.
    var revealOnSuccess: String? = nil
}

/// Where everything lives on THIS machine. BreadBox is a personal tool for a
/// personal machine — paths are configuration, not discovery.
enum Config {
    /// Shaun's working checkout (the one Xcode builds from).
    static let checkout = "\(NSHomeDirectory())/Developer/Daily_Bread"
    /// POSTGRES_* / JWT secrets for dotnet commands — parsed, never printed.
    static let envFile = "\(checkout)/.env"
    /// pg_dump archives; outside the repo so git never sees them.
    static let backups = "\(NSHomeDirectory())/DailyBreadBackups"
    /// Screen-walker screenshot exports.
    static let walks = "\(NSHomeDirectory())/Desktop/DailyBread Walks"
    /// Stable Xcode's actool crashes on the Icon Composer app icon
    /// (argument-order bug, mapped 2026-08-03) — builds require the beta.
    static let xcode = "/Applications/Xcode-beta.app"
    static let serverLog = "\(NSHomeDirectory())/Library/Logs/DailyBread-server.log"
    /// This machine's simulators (xcrun simctl list devices).
    static let kidSim = "C3D85F83-F11F-47A6-9C07-3CBE4199CF64"
    static let parentSim = "B2BA5196-62C3-40E4-82B0-3C0F79C776CD"
    static let dbContainer = "dailybread-postgres-dev"
    static let healthURL = URL(string: "http://127.0.0.1:5100/api/v1/health")!
}

/// Runs one task at a time and streams its output into the log. Serialized on
/// purpose: an admin panel that lets "reset the database" race "back up the
/// database" is a trap, not a tool.
@MainActor
@Observable
final class CommandCenter {
    var log = "🍞 BreadBox ready.\n"
    var runningTask: TaskSpec?
    var pendingConfirmation: TaskSpec?

    // Status row — refreshed on a timer and after every task.
    var serverUp = false
    var postgresUp = false
    var gitSummary = "checking…"

    var isBusy: Bool { runningTask != nil }

    // MARK: - Running tasks

    /// Entry point for every button. Destructive specs detour through the
    /// confirmation alert; ContentView calls `runConfirmed` from its OK.
    func request(_ spec: TaskSpec) {
        guard !isBusy else { return }
        if spec.confirm != nil {
            pendingConfirmation = spec
        } else {
            runConfirmed(spec)
        }
    }

    func runConfirmed(_ spec: TaskSpec) {
        pendingConfirmation = nil
        guard !isBusy else { return }
        runningTask = spec
        append("\n▶ \(spec.title)\n")

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/zsh")
        // -l gives the login PATH (Homebrew); the script itself decides cwd.
        process.arguments = ["-lc", spec.script]
        process.environment = Self.environmentWithSecrets()

        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { handle in
            let data = handle.availableData
            guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else { return }
            Task { @MainActor in self.append(text) }
        }
        process.terminationHandler = { proc in
            let status = proc.terminationStatus
            Task { @MainActor in
                pipe.fileHandleForReading.readabilityHandler = nil
                self.append(status == 0 ? "✔ done\n" : "✖ exited \(status)\n")
                if status == 0, let reveal = spec.revealOnSuccess {
                    NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: reveal)])
                }
                self.runningTask = nil
                await self.refreshStatus()
            }
        }

        do {
            try process.run()
        } catch {
            append("✖ couldn't start: \(error.localizedDescription)\n")
            runningTask = nil
        }
    }

    private func append(_ text: String) {
        log += text
        // A log pane, not an archive — keep the last ~4000 lines.
        if log.count > 400_000 {
            log = String(log.suffix(300_000))
        }
    }

    func clearLog() { log = "" }

    // MARK: - Secrets

    /// The repo's .env (POSTGRES_*, JWT) merged over the inherited
    /// environment so dotnet ef and the server see the same credentials the
    /// containers were created with. Values never touch the log.
    nonisolated static func environmentWithSecrets() -> [String: String] {
        var env = ProcessInfo.processInfo.environment
        env["DEVELOPER_DIR"] = Config.xcode
        guard let raw = try? String(contentsOfFile: Config.envFile, encoding: .utf8) else { return env }
        for line in raw.split(separator: "\n") {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            guard !trimmed.isEmpty, !trimmed.hasPrefix("#"),
                  let eq = trimmed.firstIndex(of: "=") else { continue }
            let key = String(trimmed[..<eq])
            var value = String(trimmed[trimmed.index(after: eq)...])
            value = value.trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))
            env[key] = value
        }
        return env
    }

    // MARK: - Status row

    func refreshStatus() async {
        async let health: Bool = Self.checkHealth()
        async let docker: Bool = Self.shellSucceeds("docker inspect -f '{{.State.Running}}' \(Config.dbContainer) 2>/dev/null | grep -q true")
        async let git: String = Self.shellOutput(
            "cd \(Config.checkout) && git fetch origin -q 2>/dev/null; " +
            "git status -sb | head -1")
        serverUp = await health
        postgresUp = await docker
        gitSummary = await git.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    nonisolated private static func checkHealth() async -> Bool {
        var request = URLRequest(url: Config.healthURL)
        request.timeoutInterval = 2
        guard let (_, response) = try? await URLSession.shared.data(for: request),
              let http = response as? HTTPURLResponse else { return false }
        return http.statusCode == 200
    }

    nonisolated private static func shellSucceeds(_ script: String) async -> Bool {
        await withCheckedContinuation { continuation in
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/zsh")
            process.arguments = ["-lc", script]
            process.standardOutput = FileHandle.nullDevice
            process.standardError = FileHandle.nullDevice
            process.terminationHandler = { continuation.resume(returning: $0.terminationStatus == 0) }
            do { try process.run() } catch { continuation.resume(returning: false) }
        }
    }

    nonisolated private static func shellOutput(_ script: String) async -> String {
        await withCheckedContinuation { continuation in
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/zsh")
            process.arguments = ["-lc", script]
            let pipe = Pipe()
            process.standardOutput = pipe
            process.standardError = FileHandle.nullDevice
            process.terminationHandler = { _ in
                let data = pipe.fileHandleForReading.readDataToEndOfFile()
                continuation.resume(returning: String(data: data, encoding: .utf8) ?? "")
            }
            do { try process.run() } catch { continuation.resume(returning: "unavailable") }
        }
    }
}
