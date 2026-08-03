import XCTest
@testable import DailyBreadKit

/// §3.3 — the can't-brick rules, pinned. A theme file may look wrong; it may
/// never crash, blank, or block the app.
final class ThemeLoaderTests: XCTestCase {

    // Rule 2: unknown and misspelled keys are ignored, never fatal — the typo
    // degrades to the scheme default instead of failing.
    func testMisspelledKeyDegradesToDefault() throws {
        let yaml = """
        meta: { id: test, name: Test, scheme: dark }
        colors:
          acent: "#FF0000"
        """
        let palette = try XCTUnwrap(ThemeLoader.parse(yaml).get())
        XCTAssertEqual(palette.accentHex, 0x5B9BE0, "misspelled accent inherits Harbor's")
    }

    // Rule 2: missing keys inherit from the base theme for the scheme.
    func testMissingKeysInheritSchemeBase() throws {
        let light = try XCTUnwrap(ThemeLoader.parse("meta: { id: a, name: A }").get())
        XCTAssertFalse(light.isDark)
        XCTAssertEqual(light.cardHex, 0xFFFFFF)

        let dark = try XCTUnwrap(ThemeLoader.parse("meta: { id: b, name: B, scheme: dark }").get())
        XCTAssertTrue(dark.isDark)
        XCTAssertEqual(dark.cardHex, 0x2A3852, "dark base is Harbor")
    }

    // Rule 3: the only true parse failure is malformed YAML — and it reports
    // a line, is listed, and is never selectable (palette == nil).
    func testMalformedYAMLFailsWithLine() {
        let yaml = """
        meta:
          id: broken
          name: "Unclosed
        """
        switch ThemeLoader.parse(yaml) {
        case .success: XCTFail("malformed YAML must not produce a palette")
        case .failure(let error): XCTAssertTrue(error.message.contains("Line"), error.message)
        }
    }

    // meta.id / meta.name are the only required keys.
    func testMissingMetaIsInvalid() {
        switch ThemeLoader.parse("colors: { accent: \"#123456\" }") {
        case .success: XCTFail("meta-less file must be invalid")
        case .failure(let error): XCTAssertTrue(error.message.contains("meta.id"), error.message)
        }
    }

    // §3.4: without unlock: true the invariants block is silently ignored.
    func testInvariantsRequireExplicitUnlock() throws {
        let locked = try XCTUnwrap(ThemeLoader.parse("""
        meta: { id: l, name: L }
        invariants: { gold: "#000000" }
        """).get())
        XCTAssertNil(locked.goldHex, "gold means money unless explicitly unlocked")

        let unlocked = try XCTUnwrap(ThemeLoader.parse("""
        meta: { id: u, name: U }
        invariants: { unlock: true, gold: "#112233" }
        """).get())
        XCTAssertEqual(unlocked.goldHex, 0x112233)
    }

    // THEME_OWNERSHIP: one background colour is enough — the two-stop gradient
    // is derived so a kid never has to think about gradients.
    func testSingleBackgroundDerivesGradient() throws {
        let palette = try XCTUnwrap(ThemeLoader.parse("""
        meta: { id: g, name: G, scheme: dark }
        colors: { background: "#1B2340" }
        """).get())
        XCTAssertNotEqual(palette.backgroundTopHex, palette.backgroundBottomHex)
        XCTAssertTrue(palette.backgroundTopHex != 0x1B2340 || palette.backgroundBottomHex != 0x1B2340)
    }

    // Bad hexes are missing keys, not errors.
    func testBadHexInherits() throws {
        let palette = try XCTUnwrap(ThemeLoader.parse("""
        meta: { id: h, name: H }
        colors: { accent: "notahex" }
        """).get())
        XCTAssertEqual(palette.accentHex, 0xC7284F, "Sunroom accent")
    }

    // The built-in reference exports and the example must themselves parse —
    // the breadcrumb can't teach broken YAML.
    func testReferenceExportsParse() {
        for theme in DBTheme.allCases {
            let yaml = ThemeLoader.referenceYAML(for: theme)
            if case .failure(let m) = ThemeLoader.parse(yaml) {
                XCTFail("builtin reference \(theme.rawValue) invalid: \(m.message)")
            }
        }
        if case .failure(let m) = ThemeLoader.parse(ThemeLoader.exampleYAML) {
            XCTFail("example.yaml invalid: \(m)")
        }
    }
}
