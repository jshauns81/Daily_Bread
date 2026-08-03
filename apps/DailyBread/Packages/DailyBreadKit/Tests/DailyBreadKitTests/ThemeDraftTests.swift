import XCTest
@testable import DailyBreadKit

/// §3.6 — Simple mode and YAML mode edit the same draft, so switching between
/// them must not lose or change a value. These pin that claim, plus the lint
/// that's supposed to teach rather than just say no.
final class ThemeDraftTests: XCTestCase {

    private func sample() -> ThemeDraft {
        ThemeDraft(id: "victors-blue", name: "Victor's Blue", mood: "cool and quiet",
                   author: "victor", isDark: true, accentHex: 0x4C8DFF,
                   secondaryHex: 0x3FC9B0, cardHex: 0x26304F, backgroundHex: 0x1B2340)
    }

    // Simple → YAML → Simple changes nothing a user can see.
    func testRoundTripIsLossless() throws {
        let original = sample()
        let reparsed = try XCTUnwrap(ThemeDraft(yaml: original.render()))

        XCTAssertEqual(reparsed.id, original.id)
        XCTAssertEqual(reparsed.name, original.name)
        XCTAssertEqual(reparsed.mood, original.mood)
        XCTAssertEqual(reparsed.isDark, original.isDark)
        XCTAssertEqual(reparsed.accentHex, original.accentHex)
        XCTAssertEqual(reparsed.secondaryHex, original.secondaryHex)
        XCTAssertEqual(reparsed.cardHex, original.cardHex)
        XCTAssertEqual(reparsed.backgroundHex, original.backgroundHex,
                       "the authored background must survive, not the derived stop")
    }

    // Every draft renders YAML the loader accepts — the editor cannot produce
    // a file the picker would then refuse (§3.3 rule 4).
    func testRenderedDraftAlwaysParses() {
        for isDark in [true, false] {
            var draft = sample()
            draft.isDark = isDark
            draft.unlockInvariants = false
            if case .failure(let error) = ThemeLoader.parse(draft.render()) {
                XCTFail("rendered draft invalid: \(error.message)")
            }
        }
    }

    // §3.4 — the unlock survives the round trip in both positions.
    func testInvariantUnlockRoundTrips() throws {
        var draft = sample()
        draft.unlockInvariants = true
        draft.goldHex = 0x112233
        let reparsed = try XCTUnwrap(ThemeDraft(yaml: draft.render()))
        XCTAssertTrue(reparsed.unlockInvariants)
        XCTAssertEqual(reparsed.goldHex, 0x112233)

        draft.unlockInvariants = false
        let locked = try XCTUnwrap(ThemeDraft(yaml: draft.render()))
        XCTAssertFalse(locked.unlockInvariants, "the commented-out block must stay inert")
    }

    // Seeding from a built-in starts you somewhere that already works, with a
    // blank id so you can't silently overwrite the theme you copied.
    func testSeedFromBuiltinLeavesIdBlank() {
        let draft = ThemeDraft(seededFrom: .builtin(.harbor), author: "shaun")
        XCTAssertTrue(draft.id.isEmpty)
        XCTAssertEqual(draft.name, "Harbor copy")
        XCTAssertTrue(draft.isDark)
        XCTAssertEqual(draft.accentHex, 0x5B9BE0)
    }

    // §3.6 — errors that teach: a near-miss key gets a suggestion, not a shrug.
    func testLintSuggestsTheKeyYouMeant() {
        let notes = ThemeLoader.lint("""
        meta: { id: a, name: A }
        colors:
          acent: "#FF0000"
        """)
        let suggestion = notes.first { $0.message.contains("accent") }
        XCTAssertNotNil(suggestion, "should suggest `accent` for `acent`")
        XCTAssertFalse(suggestion!.isFatal, "an unknown key is advisory, not fatal")
        XCTAssertEqual(suggestion?.line, 3)
    }

    // Keys the schema accepts but doesn't act on yet must not nag.
    func testLintStaysQuietAboutInertSchemaKeys() {
        let notes = ThemeLoader.lint("""
        meta: { id: a, name: A }
        typography:
          face: rounded
        motion:
          scale: 1.0
        """)
        XCTAssertTrue(notes.isEmpty, "inert-but-valid keys must not warn: \(notes)")
    }

    // Malformed YAML is the one fatal case, and Save is gated on it.
    func testLintMarksMalformedYAMLFatal() {
        let notes = ThemeLoader.lint("meta:\n  id: a\n  name: \"unclosed")
        XCTAssertTrue(notes.contains { $0.isFatal })
    }

    // §3.3a — contrast is advisory, and it has to actually be right.
    func testContrastFlagsUnreadableCards() {
        var bad = sample()
        bad.isDark = true
        bad.cardHex = 0xEEEEEE       // white-ish card under white label
        XCTAssertFalse(ThemeContrast.passesAA(bad.palette))

        var good = sample()
        good.isDark = true
        good.cardHex = 0x26304F
        XCTAssertTrue(ThemeContrast.passesAA(good.palette))
    }
}
