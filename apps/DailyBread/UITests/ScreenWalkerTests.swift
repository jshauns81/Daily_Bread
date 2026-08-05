import XCTest

/// Walks every reachable screen and attaches a screenshot of each — the
/// automated version of "screenshot everything for both accounts".
///
/// It rides whatever session the simulator already holds: tokens live in the
/// Keychain, so a device that's signed in as a parent walks the parent shell
/// and one signed in as a child walks the child shell. No credentials in here,
/// ever — on a signed-out device it captures the login screen and stops.
///
/// Run on a specific device:
///   xcodebuild test -project DailyBread.xcodeproj -scheme DailyBread \
///     -destination 'platform=iOS Simulator,id=<udid>' -resultBundlePath walk.xcresult
/// Screenshots land in the result bundle as attachments.
final class ScreenWalkerTests: XCTestCase {

    override func setUpWithError() throws {
        continueAfterFailure = true
    }

    func testWalkEveryScreen() throws {
        let app = XCUIApplication()
        app.launch()

        // Give bootstrap a beat to pick its screen.
        _ = app.buttons.firstMatch.waitForExistence(timeout: 5)

        guard !app.textFields["Username"].exists else {
            snap(app, "00-login")
            throw XCTSkip("No signed-in session on this device; captured the login screen only.")
        }

        snap(app, "01-home")
        scrollAndSnap(app, "01-home")

        // Tabs from both shells; each device only has its own subset.
        let tabs = ["Today", "Activity", "Earnings", "Planner", "Awards", "Approvals", "Settings"]
        var index = 2
        for tab in tabs {
            let button = app.buttons[tab]
            guard button.waitForExistence(timeout: 2), button.isHittable else { continue }
            button.tap()
            let name = String(format: "%02d-%@", index, tab.lowercased())
            pause(1.0)
            snap(app, name)
            scrollAndSnap(app, name)
            index += 1
        }

        if app.buttons["Home"].exists { app.buttons["Home"].tap() }
    }

    /// Repro probe: the walker found that tapping Settings stops the app ever
    /// idling again. This opens Settings and holds the app there so a `sample`
    /// from outside can catch the pinned main thread in the act.
    func testSettingsHold() throws {
        let app = XCUIApplication()
        app.launch()
        let settings = app.buttons["Settings"]
        guard settings.waitForExistence(timeout: 10) else {
            throw XCTSkip("No Settings tab — signed out?")
        }
        settings.tap()
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 45))
        snap(app, "settings-after-hold")
    }

    /// Repro: deleting a user theme via the row's context menu, then a
    /// relaunch to prove it doesn't resurrect through sync.
    func testDeleteUserTheme() throws {
        let app = XCUIApplication()
        app.launch()
        let settings = app.buttons["Settings"]
        guard settings.waitForExistence(timeout: 10) else {
            throw XCTSkip("No signed-in session on this device.")
        }
        settings.tap()
        pause(1.5)

        app.staticTexts["Theme"].tap()   // expand the picker
        pause(1.0)
        snap(app, "del-1-picker-open")

        // User themes sit at the top now, but scroll a little anyway — a
        // lazy List doesn't expose off-screen rows to accessibility at all
        // (which is exactly how the first version of this test got fooled).
        let target = app.staticTexts.matching(
            NSPredicate(format: "label CONTAINS[c] 'copy'")).firstMatch
        var attempts = 0
        while !target.exists && attempts < 4 {
            app.swipeUp()
            pause(0.5)
            attempts += 1
        }
        guard target.exists else {
            snap(app, "del-x-no-user-theme")
            throw XCTSkip("No user theme with 'copy' in its name to delete.")
        }
        let name = target.label

        let row = app.cells.containing(
            NSPredicate(format: "label CONTAINS[c] 'copy'")).firstMatch
        let options = (row.exists ? row.buttons["Theme options"] : app.buttons["Theme options"])
        guard options.waitForExistence(timeout: 3) else {
            snap(app, "del-x-no-options-button")
            return XCTFail("No visible Theme options button on the row")
        }
        options.tap()
        pause(0.8)
        snap(app, "del-2-options-menu")

        let delete = app.buttons["Delete"]
        guard delete.waitForExistence(timeout: 3) else {
            snap(app, "del-x-no-menu")
            return XCTFail("Options menu has no Delete item")
        }
        delete.tap()
        pause(1.5)
        snap(app, "del-3-after-delete")
        XCTAssertFalse(app.staticTexts[name].exists, "Row still present after delete")

        // Resurrection check: relaunch → Settings → expanded picker.
        app.terminate()
        app.launch()
        _ = app.buttons["Settings"].waitForExistence(timeout: 10)
        app.buttons["Settings"].tap()
        pause(2.0)
        app.staticTexts["Theme"].tap()
        pause(1.5)
        snap(app, "del-4-after-relaunch")
        XCTAssertFalse(app.staticTexts[name].exists, "Theme resurrected after relaunch")
    }

    /// Sweep #8 probe: scroll Awards all the way down and capture the resting
    /// state. A mid-scroll frame passing under the floating tab bar is by
    /// design; the bug (if real) is the LAST row still buried when the scroll
    /// view has nothing left to give.
    func testAwardsBottomClearance() throws {
        let app = XCUIApplication()
        app.launch()
        let awards = app.buttons["Awards"]
        guard awards.waitForExistence(timeout: 10), awards.isHittable else {
            throw XCTSkip("No Awards tab — parent shell or signed out.")
        }
        awards.tap()
        pause(1.5)
        for _ in 0..<8 {
            app.swipeUp()
            pause(0.4)
        }
        pause(1.0)   // let the rubber-band settle
        snap(app, "awards-bottom")
    }

    /// One swipe up per tab: catches content below the fold without turning
    /// the walk into a flaky scroll marathon.
    private func scrollAndSnap(_ app: XCUIApplication, _ name: String) {
        app.swipeUp()
        pause(0.6)
        snap(app, name + "-scrolled")
        app.swipeDown()
        pause(0.4)
    }

    private func pause(_ seconds: TimeInterval) {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: seconds))
    }

    private func snap(_ app: XCUIApplication, _ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
