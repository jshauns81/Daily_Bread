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
