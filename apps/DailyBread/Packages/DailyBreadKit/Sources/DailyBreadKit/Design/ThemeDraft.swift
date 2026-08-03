import SwiftUI

// §3.6 — the editor's shared model. Simple mode and YAML mode edit THE SAME
// draft, so switching is lossless in both directions: drag a colour well and
// the YAML updates, fix a hex in the text and the well moves.

/// The values a theme actually carries, in editable form. Deliberately not the
/// manifest: a draft always has a value for every field (seeded from the active
/// theme), so Simple mode never has to render an empty well.
public struct ThemeDraft: Hashable, Sendable {
    public var id: String
    public var name: String
    public var mood: String
    public var author: String
    public var isDark: Bool
    public var accentHex: UInt32
    public var secondaryHex: UInt32
    public var cardHex: UInt32
    public var backgroundHex: UInt32
    /// §3.4 — off by default; the editor makes the unlock explicit and loud.
    public var unlockInvariants: Bool
    public var goldHex: UInt32
    public var helpHex: UInt32

    public init(id: String = "", name: String = "", mood: String = "", author: String = "",
                isDark: Bool = false, accentHex: UInt32 = 0xC7284F,
                secondaryHex: UInt32 = 0x2E8C86, cardHex: UInt32 = 0xFFFFFF,
                backgroundHex: UInt32 = 0xFFFDF9, unlockInvariants: Bool = false,
                goldHex: UInt32 = 0xE7B44A, helpHex: UInt32 = 0xF06B6B) {
        self.id = id; self.name = name; self.mood = mood; self.author = author
        self.isDark = isDark; self.accentHex = accentHex; self.secondaryHex = secondaryHex
        self.cardHex = cardHex; self.backgroundHex = backgroundHex
        self.unlockInvariants = unlockInvariants; self.goldHex = goldHex; self.helpHex = helpHex
    }

    /// §3.6 "seeded from the current theme" — opening the editor on Harbor
    /// pre-fills Harbor's values, id blank and name "Harbor copy". You start
    /// from something that already works and looks right.
    public init(seededFrom theme: AppTheme, author: String) {
        switch theme {
        case .builtin(let t):
            let hexes = t.draftHexes
            self.init(id: "", name: "\(t.displayName) copy", mood: t.mood, author: author,
                      isDark: t.isDark, accentHex: hexes.accent, secondaryHex: hexes.secondary,
                      cardHex: hexes.card, backgroundHex: hexes.background)
        case .custom(let p):
            self.init(id: "", name: "\(p.name) copy", mood: p.mood, author: author,
                      isDark: p.isDark, accentHex: p.accentHex, secondaryHex: p.secondaryHex,
                      cardHex: p.cardHex,
                      backgroundHex: p.backgroundBaseHex ?? p.backgroundTopHex,
                      unlockInvariants: p.goldHex != nil || p.helpHex != nil,
                      goldHex: p.goldHex ?? 0xE7B44A, helpHex: p.helpHex ?? 0xF06B6B)
        }
    }

    /// Editing an existing user theme keeps its identity.
    public init(editing palette: CustomPalette, author: String) {
        self.init(id: palette.id, name: palette.name, mood: palette.mood, author: author,
                  isDark: palette.isDark, accentHex: palette.accentHex,
                  secondaryHex: palette.secondaryHex, cardHex: palette.cardHex,
                  backgroundHex: palette.backgroundBaseHex ?? palette.backgroundTopHex,
                  unlockInvariants: palette.goldHex != nil || palette.helpHex != nil,
                  goldHex: palette.goldHex ?? 0xE7B44A, helpHex: palette.helpHex ?? 0xF06B6B)
    }

    /// A draft always resolves — that's what makes live preview safe.
    public var palette: CustomPalette {
        let manifest = self.manifest
        return manifest.resolved() ?? CustomPalette(
            id: id.isEmpty ? "draft" : id, name: name.isEmpty ? "Untitled" : name,
            mood: mood, isDark: isDark, accentHex: accentHex, secondaryHex: secondaryHex,
            cardHex: cardHex,
            backgroundTopHex: ThemeHex.shiftLightness(backgroundHex, by: isDark ? 0.035 : 0.02),
            backgroundBottomHex: ThemeHex.shiftLightness(backgroundHex, by: isDark ? -0.035 : -0.045),
            backgroundBaseHex: backgroundHex,
            onAccentHex: 0xFFFFFF,
            goldHex: unlockInvariants ? goldHex : nil,
            helpHex: unlockInvariants ? helpHex : nil)
    }

    public var manifest: ThemeManifest {
        ThemeManifest(
            meta: .init(id: id.isEmpty ? nil : id, name: name.isEmpty ? nil : name,
                        mood: mood.isEmpty ? nil : mood,
                        author: author.isEmpty ? nil : author,
                        scheme: isDark ? "dark" : "light"),
            colors: .init(background: .init(top: ThemeHex.format(backgroundHex)),
                          card: ThemeHex.format(cardHex),
                          accent: ThemeHex.format(accentHex),
                          secondary: ThemeHex.format(secondaryHex)),
            invariants: unlockInvariants
                ? .init(unlock: true, gold: ThemeHex.format(goldHex), help: ThemeHex.format(helpHex))
                : nil)
    }

    public init?(yaml: String) {
        guard case .success(let palette) = ThemeLoader.parse(yaml) else { return nil }
        self.init(editing: palette, author: "")
    }

    /// §3.6 — the template lists EVERY key, each with a trailing comment naming
    /// what it affects. Self-documenting beats a reference nobody opens.
    public func render() -> String {
        var out = """
        meta:
          id: \(id.isEmpty ? "my-theme" : id)\(id.isEmpty ? "        # required — short, unique, no spaces" : "")
          name: \(name.isEmpty ? "My Theme" : name)
          mood: \(mood.isEmpty ? "make it yours" : mood)   # shows under the name in the picker
          scheme: \(isDark ? "dark" : "light")           # dark or light — the whole app follows

        colors:
          background: "\(ThemeHex.format(backgroundHex))"  # the app's ground (the gradient is derived)
          card:       "\(ThemeHex.format(cardHex))"  # every card, sheet and row surface
          accent:     "\(ThemeHex.format(accentHex))"  # buttons, links, selected states
          secondary:  "\(ThemeHex.format(secondaryHex))"  # progress fills, secondary chips
        """
        if unlockInvariants {
            out += """


            # Gold means money and red means Help in EVERY theme. You've unlocked
            # them here on purpose — that's allowed, but it's yours to own.
            invariants:
              unlock: true
              gold: "\(ThemeHex.format(goldHex))"
              help: "\(ThemeHex.format(helpHex))"
            """
        } else {
            out += """


            # Gold means money and red means Help in every theme — on purpose.
            # To change them anyway, uncomment this and keep `unlock: true`:
            #
            # invariants:
            #   unlock: true
            #   gold: "#E7B44A"
            #   help: "#F06B6B"
            """
        }
        return out + "\n"
    }
}

private extension DBTheme {
    var draftHexes: (accent: UInt32, secondary: UInt32, card: UInt32, background: UInt32) {
        switch self {
        case .sunroom: return (0xC7284F, 0x2E8C86, 0xFFFFFF, 0xFFFDF9)
        case .sky: return (0x3D7BE0, 0x4BA39C, 0xFFFFFF, 0xFBFCFF)
        case .rosewater: return (0xD24E86, 0x6BA3C6, 0xFFFFFF, 0xFFF9FB)
        case .meadow: return (0x3E9E6B, 0x8AA83E, 0xFFFFFF, 0xF8FBF6)
        case .mulberry: return (0xEA6E92, 0x2E8C86, 0x4A2237, 0x3E1B30)
        case .harbor: return (0x5B9BE0, 0x4BA39C, 0x2A3852, 0x223049)
        }
    }
}

// MARK: - Contrast (§3.3a — advisory, never blocking)

public enum ThemeContrast {
    /// WCAG relative luminance.
    public static func luminance(_ hex: UInt32) -> Double {
        func channel(_ v: Double) -> Double {
            v <= 0.03928 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4)
        }
        let r = channel(Double((hex >> 16) & 0xFF) / 255)
        let g = channel(Double((hex >> 8) & 0xFF) / 255)
        let b = channel(Double(hex & 0xFF) / 255)
        return 0.2126 * r + 0.7152 * g + 0.0722 * b
    }

    public static func ratio(_ a: UInt32, _ b: UInt32) -> Double {
        let la = luminance(a), lb = luminance(b)
        return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)
    }

    /// The label colour the app will actually draw on a card in this palette.
    public static func labelHex(isDark: Bool) -> UInt32 {
        isDark ? 0xFFFFFF : 0x000000
    }

    /// §3.3a — "if label on card falls below 4.5:1, warn; don't block. It's
    /// their app." An unreadable theme is still recoverable via §3.3 rule 6.
    public static func labelOnCard(_ palette: CustomPalette) -> Double {
        ratio(labelHex(isDark: palette.isDark), palette.cardHex)
    }

    public static func passesAA(_ palette: CustomPalette) -> Bool {
        labelOnCard(palette) >= 4.5
    }
}
