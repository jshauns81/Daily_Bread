import XCTest

/// The 2026-08-07 report, reproduced from zero: a device that has never run
/// the app, through server setup and a parent sign-in, then the same
/// tap-response probes as `TouchProbeTests`.
///
/// What the first pass of this test found: on the FIRST process after
/// sign-in, the Planner's toolbar/segments and Settings' rows are absent
/// from the accessibility tree while the tab bar works — and a relaunch with
/// the same session shows everything. This version instruments that state:
/// screenshots and tree dumps either way, coordinate taps (pure touch
/// synthesis, no accessibility lookup — what a finger does) against the
/// rendered controls, then an in-place relaunch to prove or disprove the
/// heal.
///
/// Run against an ERASED simulator with the dev server on localhost:5100.
/// The credentials below are `DevDataSeeder.cs` constants for the local dev
/// database — fixtures, not secrets; they exist in the repo in plain text
/// and guard nothing outside this machine.
final class FreshInstallProbeTests: XCTestCase {

    private static let devServer = "http://localhost:5100"
    private static let username = "parent_test"
    private static let password = "Parent123!"

    override func setUpWithError() throws {
        continueAfterFailure = true
        // Xcode Cloud runs this scheme's tests with no dev server to talk to;
        // there the whole flow is unrunnable, not failing. Skip unless the
        // local server answers.
        try XCTSkipUnless(Self.devServerReachable,
                          "No dev server at \(Self.devServer) — local-only probe.")
    }

    /// Probed once per test process; SignInCycleProbeTests reads it too.
    static let devServerReachable = devServerAnswers()

    private static func devServerAnswers() -> Bool {
        guard let url = URL(string: devServer + "/api/v1/health") else { return false }
        var ok = false
        let done = DispatchSemaphore(value: 0)
        URLSession.shared.dataTask(with: url) { _, response, _ in
            ok = (response as? HTTPURLResponse)?.statusCode == 200
            done.signal()
        }.resume()
        _ = done.wait(timeout: .now() + 4)
        return ok
    }

    private func tapsNeeded(_ element: XCUIElement,
                            limit: Int = 3,
                            effect: () -> Bool) -> Int? {
        for attempt in 1...limit {
            element.tap()
            let deadline = Date(timeIntervalSinceNow: 2.0)
            while Date() < deadline {
                if effect() { return attempt }
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.2))
            }
        }
        return nil
    }

    private func record(_ app: XCUIApplication, _ results: inout [String],
                        name: String, taps: Int?) {
        switch taps {
        case 1:
            results.append("OK        \(name)")
        case let n?:
            results.append("DEGRADED  \(name) needed \(n) taps")
            XCTFail("\(name) needed \(n) taps")
        case nil:
            results.append("DEAD      \(name) never responded")
            XCTFail("\(name) never responded to 3 taps")
        }
    }

    private func snapshot(_ app: XCUIApplication, _ name: String) {
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }

    /// One atomic `debugDescription` snapshot — enumerating live queries
    /// (`allElementsBoundByIndex`) throws when the tree mutates mid-walk,
    /// which is exactly when this diagnostic matters most.
    private func roster(_ app: XCUIApplication, _ label: String) {
        let tree = app.debugDescription
        let buttons = tree.split(separator: "\n")
            .filter { $0.contains("Button") }
            .prefix(30)
            .map { $0.trimmingCharacters(in: .whitespaces) }
        print("PROBE-ROSTER[\(label)] \(buttons.count) buttons")
        buttons.forEach { print("  PR| \($0)") }
        let attachment = XCTAttachment(string: tree)
        attachment.name = "tree-\(label)"
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    func testFirstRunThenControlsRespond() throws {
        let app = XCUIApplication()
        app.launch()

        var results: [String] = []

        // ---- First run: server setup ----
        // A just-erased simulator's first launch is slow — give bootstrap real time.
        let urlField = app.textFields.firstMatch
        guard urlField.waitForExistence(timeout: 45) else {
            snapshot(app, "no-setup-field")
            throw XCTSkip("No setup field — device not fresh, or bootstrap never landed on setup.")
        }
        // The field pre-fills "https://"; the dev server is plain http, so
        // clear it before typing. Tap at the far right to land the caret at
        // the end of the existing text.
        urlField.coordinate(withNormalizedOffset: CGVector(dx: 0.95, dy: 0.5)).tap()
        urlField.typeText(String(repeating: XCUIKeyboardKey.delete.rawValue, count: 12))
        urlField.typeText(Self.devServer)

        let connect = app.buttons["Connect"]
        record(app, &results, name: "setup-connect",
               taps: tapsNeeded(connect, effect: { app.textFields["Username"].exists }))
        guard app.textFields["Username"].exists else {
            attachAndPrint(results)
            return XCTFail("Never reached the login screen — server down or Connect dead.")
        }

        // ---- First run: sign in ----
        app.textFields["Username"].tap()
        app.textFields["Username"].typeText(Self.username)
        app.secureTextFields["Password"].tap()
        app.secureTextFields["Password"].typeText(Self.password)

        let signIn = app.buttons["Sign In"]
        let shellUp = {
            app.buttons["Planner"].exists || app.buttons["Today"].exists
                || app.staticTexts["Parent screens are protected"].exists
        }
        record(app, &results, name: "login-sign-in", taps: tapsNeeded(signIn, effect: shellUp))
        guard shellUp() else {
            attachAndPrint(results)
            return XCTFail("Never reached the shell after sign-in.")
        }

        // ---- The system "Save Password?" sheet ----
        // It presents at the exact moment the login hierarchy is being torn
        // down — over the gate wall when biometry is enrolled. The app is
        // fully inert underneath it (proved earlier, and expected); dismiss
        // it the way a person does before anything else can be tapped.
        let notNow = app.buttons["Not Now"]
        if notNow.waitForExistence(timeout: 4) {
            results.append("SHEET     save-password sheet appeared post-login")
            // Both dismissals exist in the wild: Shaun would tap Save on the
            // family phones, Not Now is the skeptic's path. DB_PROBE_SAVE=1
            // on the TEST process picks the Save branch.
            let viaSave = ProcessInfo.processInfo.environment["DB_PROBE_SAVE"] == "1"
                && app.buttons["Save"].exists
            let dismiss = viaSave ? app.buttons["Save"] : notNow
            dismiss.tap()
            let gone = !notNow.waitForExistence(timeout: 2)
            results.append(gone ? "SHEET     dismissed via \(viaSave ? "Save" : "Not Now")"
                                : "SHEET     \(viaSave ? "Save" : "Not Now") tap did NOT dismiss it")
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 1.5))
        } else {
            results.append("SHEET     no save-password sheet this run")
        }

        // ---- The gate intro wall (biometry-enrolled devices) ----
        // Her phone's exact path: sign-in → save-password sheet → wall →
        // Face ID system sheet → MainView born as that sheet dismisses.
        // The outer driver watches stdout for AWAITING-FACE-ID and sends the
        // Simulator a Matching Face (⌥⌘M); nothing in here fakes biometry.
        if !app.buttons["Planner"].waitForExistence(timeout: 5) {
            let unlock = app.buttons.matching(
                NSPredicate(format: "label BEGINSWITH 'Unlock'")).firstMatch
            guard unlock.waitForExistence(timeout: 5) else {
                attachAndPrint(results)
                return XCTFail("Neither the shell nor the gate wall appeared.")
            }
            results.append("WALL      gate wall up (intro: \(app.staticTexts["Parent screens are protected"].exists))")
            print("AWAITING-FACE-ID")
            unlock.tap()
            if app.buttons["Planner"].waitForExistence(timeout: 45) {
                results.append("WALL      passed via Face ID match")
            } else {
                results.append("WALL      never opened after unlock + match")
                roster(app, "stuck-at-wall")
                attachAndPrint(results)
                return XCTFail("Gate never opened — did the match land?")
            }
        }

        // ---- The state the family actually hit: first process, post-login ----
        app.buttons["Planner"].tap()
        _ = app.navigationBars["Planner"].waitForExistence(timeout: 5)
        // Let load + render settle so "missing" can't mean "still loading".
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 3.0))

        roster(app, "first-process")
        snapshot(app, "first-process-planner")

        let addChore = app.buttons["Add chore"]
        let editorUp = { app.buttons["Cancel"].exists || app.navigationBars["New Chore"].exists }

        if addChore.waitForExistence(timeout: 3) {
            results.append("PRESENT   first-process planner toolbar is in the tree")
            record(app, &results, name: "first-process-add-chore",
                   taps: tapsNeeded(addChore, effect: editorUp))
            if editorUp() { app.buttons["Cancel"].tap() }
        } else {
            results.append("ABSENT    first-process planner toolbar missing from tree")

            // A finger doesn't consult the accessibility tree. Tap the pixels
            // where the rendered controls are and see if TOUCH works even
            // though the tree says nothing is there. Grid rows are the
            // biggest target: mid-screen, tapping one opens the editor.
            let rowTap = app.coordinate(withNormalizedOffset: CGVector(dx: 0.5, dy: 0.42))
            rowTap.tap()
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 2.0))
            let rowWorked = editorUp()
            results.append(rowWorked
                ? "TOUCH-OK  coordinate tap on a grid row opened the editor"
                : "TOUCH-DEAD coordinate tap on a grid row did nothing")
            snapshot(app, "after-coordinate-row-tap")
            if rowWorked {
                (app.buttons["Cancel"].exists ? app.buttons["Cancel"]
                                              : app.buttons.firstMatch).tap()
            }

            // The toolbar + lives top-right; try its pixels too.
            if !editorUp() {
                app.coordinate(withNormalizedOffset: CGVector(dx: 0.93, dy: 0.08)).tap()
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 2.0))
                results.append(editorUp()
                    ? "TOUCH-OK  coordinate tap on toolbar + opened the editor"
                    : "TOUCH-DEAD coordinate tap on toolbar + did nothing")
                snapshot(app, "after-coordinate-plus-tap")
                if editorUp() { app.buttons["Cancel"].tap() }
            }
        }

        // ---- The heal: same session, new process ----
        app.terminate()
        app.launch()
        guard app.buttons["Planner"].waitForExistence(timeout: 15) else {
            results.append("RELAUNCH  shell never came back — session lost?")
            attachAndPrint(results)
            return XCTFail("Relaunch did not restore the session.")
        }
        app.buttons["Planner"].tap()
        _ = app.navigationBars["Planner"].waitForExistence(timeout: 5)
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 2.0))

        roster(app, "relaunched")
        snapshot(app, "relaunched-planner")

        if app.buttons["Add chore"].waitForExistence(timeout: 3) {
            results.append("PRESENT   relaunched planner toolbar is in the tree")
            record(app, &results, name: "relaunched-add-chore",
                   taps: tapsNeeded(app.buttons["Add chore"], effect: editorUp))
        } else {
            results.append("ABSENT    relaunched planner toolbar STILL missing")
            XCTFail("Relaunch did not heal the tree")
        }

        attachAndPrint(results)
    }

    private func attachAndPrint(_ results: [String]) {
        let report = XCTAttachment(string: results.joined(separator: "\n"))
        report.name = "touch-probe-report"
        report.lifetime = .keepAlways
        add(report)
        print("TOUCH-PROBE-REPORT\n" + results.joined(separator: "\n"))
    }
}
