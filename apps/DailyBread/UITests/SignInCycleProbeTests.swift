import XCTest

/// The dead-buttons state after sign-in, measured honestly.
///
/// v1 of this test had a hole: it tapped the Planner tab, never checked the
/// switch happened, then reported Planner controls "missing" while staring at
/// Home. The evidence underneath was still real — the tab tap did nothing in
/// 8 of 8 post-sign-in processes — but the labels lied. This version measures
/// each surface separately, with retry counts, so DEGRADED (n taps) and DEAD
/// never blur:
///
///   tab-switch   taps on the tab bar until the Planner nav bar appears
///   toolbar      taps on Add chore until the editor sheet appears
///   segment      taps on Routines until it reads selected
///
/// Cycle shape: launch (restored shell) → sign out → sign in (keyboard up,
/// exactly like a person) → measure → terminate. The restored-shell phase at
/// the top of each cycle doubles as the healthy control: its Settings tap must
/// work to reach Sign Out at all.
///
/// Uses the DevDataSeeder fixture credentials; see FreshInstallProbeTests.
final class SignInCycleProbeTests: XCTestCase {

    private static let username = "parent_test"
    private static let password = "Parent123!"
    private static let cycles = 6

    override func setUpWithError() throws {
        continueAfterFailure = true
        // Same local-only guard as FreshInstallProbeTests: without a dev
        // server (Xcode Cloud), sign-in cycles are unrunnable, not failing.
        try XCTSkipUnless(FreshInstallProbeTests.devServerReachable,
                          "No dev server — local-only probe.")
    }

    /// Taps until `effect` is true; returns the tap count, or nil if `limit`
    /// taps never produced it.
    private func tapsNeeded(_ element: XCUIElement, limit: Int = 3,
                            effect: () -> Bool) -> Int? {
        for attempt in 1...limit {
            element.tap()
            let deadline = Date(timeIntervalSinceNow: 2.5)
            while Date() < deadline {
                if effect() { return attempt }
                RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.2))
            }
        }
        return nil
    }

    private func describeTaps(_ taps: Int?) -> String {
        switch taps {
        case 1: return "ok"
        case let n?: return "DEGRADED(\(n) taps)"
        case nil: return "DEAD"
        }
    }

    func testRepeatedSignInsMeasureEverySurface() throws {
        var report: [String] = []
        var sawTrouble = false
        // Printed per cycle so a mid-run runner kill (twice tonight) cannot
        // take the completed cycles' verdicts with it.
        func note(_ s: String) { report.append(s); print("CYCLE-LINE " + s) }

        for cycle in 1...Self.cycles {
            let app = XCUIApplication()
            app.launch()

            let login = app.textFields["Username"]
            let planner = app.buttons["Planner"]
            guard login.waitForExistence(timeout: 10) || planner.waitForExistence(timeout: 10) else {
                note("C\(cycle) LOST — neither login nor shell appeared")
                break
            }

            if planner.exists {
                app.buttons["Settings"].tap()
                let signOut = app.buttons["Sign Out"]
                var scrolls = 0
                while !(signOut.exists && signOut.isHittable), scrolls < 6 {
                    app.swipeUp()
                    scrolls += 1
                }
                guard signOut.exists else {
                    note("C\(cycle) STUCK — no Sign Out control in Settings")
                    break
                }
                signOut.tap()
                guard login.waitForExistence(timeout: 10) else {
                    note("C\(cycle) STUCK — sign out never reached login")
                    break
                }
            }

            login.tap()
            login.typeText(Self.username)
            app.secureTextFields["Password"].tap()
            app.secureTextFields["Password"].typeText(Self.password)
            app.buttons["Sign In"].tap()

            guard planner.waitForExistence(timeout: 15) else {
                note("C\(cycle) STUCK — shell never appeared after sign-in")
                snapshot(app, "c\(cycle)-no-shell")
                break
            }

            // iOS re-offers the save-password sheet on EVERY sign-in, and the
            // app is legitimately inert underneath it. Dismiss it before
            // measuring, and say so — otherwise every surface reads DEAD and
            // the report is measuring the scrim, not the app.
            var sheetNote = "sheet=none"
            let notNow = app.buttons["Not Now"]
            if notNow.waitForExistence(timeout: 4) {
                notNow.tap()
                sheetNote = notNow.waitForExistence(timeout: 2)
                    ? "sheet=STUCK" : "sheet=dismissed"
            }

            // Let the transition finish before judging anything.
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 2.0))

            // Surface 1: the tab bar.
            let tabTaps = tapsNeeded(planner) {
                app.navigationBars["Planner"].exists
            }
            var line = "C\(cycle) \(sheetNote) tab-switch=\(describeTaps(tabTaps))"

            if tabTaps == nil {
                sawTrouble = true
                snapshot(app, "c\(cycle)-tab-dead")
                dumpTree(app, "c\(cycle)-tab-dead")
                note(line + "  (skipping content probes — never left Home)")
                app.terminate()
                continue
            }
            if tabTaps != 1 { sawTrouble = true }

            // Surface 2: the Planner toolbar (content controls).
            RunLoop.current.run(until: Date(timeIntervalSinceNow: 1.5))
            let editorUp = { app.buttons["Cancel"].exists || app.navigationBars["New Chore"].exists }
            if app.buttons["Add chore"].waitForExistence(timeout: 3) {
                let toolbarTaps = tapsNeeded(app.buttons["Add chore"], effect: editorUp)
                line += " toolbar=\(describeTaps(toolbarTaps))"
                if toolbarTaps != 1 {
                    sawTrouble = true
                    if toolbarTaps == nil { snapshot(app, "c\(cycle)-toolbar-dead"); dumpTree(app, "c\(cycle)-toolbar-dead") }
                }
                if editorUp() { app.buttons["Cancel"].tap() }
            } else {
                sawTrouble = true
                line += " toolbar=MISSING-FROM-TREE"
                snapshot(app, "c\(cycle)-toolbar-missing")
                dumpTree(app, "c\(cycle)-toolbar-missing")
            }

            // Surface 3: the segmented picker.
            let routines = app.buttons["Routines"]
            if routines.waitForExistence(timeout: 2) {
                let segTaps = tapsNeeded(routines) { routines.isSelected }
                line += " segment=\(describeTaps(segTaps))"
                if segTaps != 1 { sawTrouble = true }
            } else {
                sawTrouble = true
                line += " segment=MISSING-FROM-TREE"
            }

            note(line)
            app.terminate()
        }

        let text = report.joined(separator: "\n")
        let attachment = XCTAttachment(string: text)
        attachment.name = "cycle-report"
        attachment.lifetime = .keepAlways
        add(attachment)
        print("CYCLE-REPORT\n" + text)

        XCTAssertFalse(sawTrouble, "At least one surface was degraded or dead:\n\(text)")
    }

    private func snapshot(_ app: XCUIApplication, _ name: String) {
        let shot = XCTAttachment(screenshot: app.screenshot())
        shot.name = name
        shot.lifetime = .keepAlways
        add(shot)
    }

    private func dumpTree(_ app: XCUIApplication, _ name: String) {
        let attachment = XCTAttachment(string: app.debugDescription)
        attachment.name = "tree-\(name)"
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
