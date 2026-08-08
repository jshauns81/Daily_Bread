import XCTest

/// Repro harness for the 2026-08-07 family report: on a first install (iOS
/// 26.6) most content controls ignored taps while the tab bar kept working.
///
/// Every probe taps a control ONCE and waits for its expected effect. A second
/// and third tap tell "dead" apart from "needs several taps" — the latter was
/// the R4 `.simultaneousGesture` regression's signature, so if it reappears
/// here the ActivityReporter rework did not actually kill it.
///
/// On any control that never fires, the accessibility tree is attached: an
/// invisible occluder shows up there long before it shows up in a screenshot.
final class TouchProbeTests: XCTestCase {

    override func setUpWithError() throws {
        continueAfterFailure = true
    }

    /// Taps until `effect` reports true. Returns how many taps that took,
    /// or nil for a control that never responded.
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
            let tree = XCTAttachment(string: app.debugDescription)
            tree.name = "tree-after-dead-\(name)"
            tree.lifetime = .keepAlways
            add(tree)
            XCTFail("\(name) never responded to \(3) taps")
        }
    }

    func testParentContentControlsRespond() throws {
        let app = XCUIApplication()
        app.launchEnvironment["DB_DISABLE_PARENT_GATE"] = "1"
        app.launch()

        _ = app.buttons.firstMatch.waitForExistence(timeout: 5)
        guard !app.textFields["Username"].exists else {
            throw XCTSkip("No signed-in session on this device.")
        }
        guard app.buttons["Planner"].waitForExistence(timeout: 5) else {
            throw XCTSkip("No Planner tab — child shell?")
        }

        var results: [String] = []

        // The control Shaun named first when R4 shipped: Planner → New.
        app.buttons["Planner"].tap()
        let plannerNavUp = app.navigationBars["Planner"].waitForExistence(timeout: 5)
        // Which screen actually came up, and what the tree offers — printed to
        // stdout so a failed run explains itself without a result bundle.
        print("PROBE-NAV planner=\(plannerNavUp) bars=\(app.navigationBars.allElementsBoundByIndex.map(\.identifier))")
        print("PROBE-BUTTONS \(app.buttons.allElementsBoundByIndex.prefix(40).map(\.label))")

        let addChore = app.buttons["Add chore"]
        if addChore.waitForExistence(timeout: 3) {
            let editorUp = { app.buttons["Cancel"].exists || app.navigationBars["New Chore"].exists }
            record(app, &results, name: "planner-add-chore",
                   taps: tapsNeeded(addChore, effect: editorUp))
            if editorUp() {
                let cancel = app.buttons["Cancel"]
                record(app, &results, name: "editor-cancel",
                       taps: tapsNeeded(cancel, effect: { !editorUp() }))
            }
        } else {
            results.append("MISSING   planner-add-chore not found")
            XCTFail("Add chore button not found at all")
        }

        // The mode switch (Tasks/Routines segmented picker).
        let routines = app.buttons["Routines"].exists
            ? app.buttons["Routines"]
            : app.segmentedControls.buttons["Routines"]
        if routines.waitForExistence(timeout: 2) {
            record(app, &results, name: "planner-kind-picker",
                   taps: tapsNeeded(routines, effect: { routines.isSelected }))
        } else {
            results.append("MISSING   planner-kind-picker")
        }

        // The grid/list toggle.
        let toggle = app.buttons["List view"].exists ? app.buttons["List view"]
                                                     : app.buttons["Grid view"]
        if toggle.exists {
            let before = toggle.label
            record(app, &results, name: "planner-view-toggle",
                   taps: tapsNeeded(toggle, effect: {
                       (app.buttons["List view"].exists ? "List view" : "Grid view") != before
                   }))
        } else {
            results.append("MISSING   planner-view-toggle")
        }

        // A Settings toggle — different tab, different container kind.
        app.buttons["Settings"].tap()
        _ = app.navigationBars["Settings"].waitForExistence(timeout: 5)
        let themeRow = app.staticTexts["Theme"]
        if themeRow.waitForExistence(timeout: 3) {
            record(app, &results, name: "settings-theme-row",
                   taps: tapsNeeded(themeRow, effect: {
                       // Expanding the picker reveals theme names.
                       app.staticTexts["Sunroom"].exists || app.cells.count > 8
                   }))
        } else {
            results.append("MISSING   settings-theme-row")
        }

        let report = XCTAttachment(string: results.joined(separator: "\n"))
        report.name = "touch-probe-report"
        report.lifetime = .keepAlways
        add(report)
        print("TOUCH-PROBE-REPORT\n" + results.joined(separator: "\n"))
    }
}
