import SwiftUI
#if os(iOS)
import UIKit
#else
import AppKit
#endif

// §3.6's two implementation notes, both of which would otherwise bite:
//
// 1. Smart text features MUST be off. iOS and macOS substitute curly quotes and
//    en-dashes into YAML and silently break parsing — the single most likely
//    source of "it says line 7 is broken and it looks fine to me". SwiftUI's
//    TextEditor exposes no way to turn quote/dash substitution off, so the real
//    text view is bridged for that reason alone.
// 2. The accessory bar needs the CURSOR, not the end of the document, so its
//    keys insert where you're typing.
//
// Scrolling is disabled and the height is driven from the line count so the
// colour gutter beside it can align to text rows with no per-line measurement —
// the payoff §3.6 names for choosing SF Mono (fixed advance, slotted zero).

/// SF Mono where it exists, the system monospace otherwise. The ONE place a
/// monospaced face appears in Daily Bread: it signals "this is the code
/// surface" without needing a label.
enum YAMLFont {
    #if os(iOS)
    static let size: CGFloat = 15   // below this, hex on a phone is a squint
    static var platform: UIFont {
        UIFont(name: "SFMono-Regular", size: size)
            ?? .monospacedSystemFont(ofSize: size, weight: .regular)
    }
    #else
    static let size: CGFloat = 13
    static var platform: NSFont {
        NSFont(name: "SFMono-Regular", size: size)
            ?? .monospacedSystemFont(ofSize: size, weight: .regular)
    }
    #endif

    /// Line height the gutter mirrors.
    static var lineHeight: CGFloat { (platform.ascender - platform.descender + platform.leading).rounded(.up) }
    static let insets: CGFloat = 8
}

/// Syntax colouring, recomputed per edit — fine at 40 lines, and `AttributedString`
/// is all §3.6 asks for. Comments recede, keys read as structure, hexes glow.
enum YAMLHighlight {
    static func attributed(_ text: String, accent: PlatformColor, label: PlatformColor) -> NSAttributedString {
        let full = NSMutableAttributedString(string: text, attributes: [
            .font: YAMLFont.platform,
            .foregroundColor: label
        ])
        let ns = text as NSString

        func apply(_ pattern: String, _ color: PlatformColor) {
            guard let regex = try? NSRegularExpression(pattern: pattern) else { return }
            for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
                full.addAttribute(.foregroundColor, value: color, range: match.range)
            }
        }

        apply("\"?#[0-9A-Fa-f]{6}\"?", accent)                    // hex values
        apply("^[ \\t]*[A-Za-z_][A-Za-z0-9_]*(?=:)", accent)      // keys
        apply("(?m)#(?![0-9A-Fa-f]{6}).*$", PlatformColor.secondaryLabelCompat)  // comments
        return full
    }
}

#if os(iOS)
typealias PlatformColor = UIColor
extension UIColor { static var secondaryLabelCompat: UIColor { .secondaryLabel } }
#else
typealias PlatformColor = NSColor
extension NSColor { static var secondaryLabelCompat: NSColor { .secondaryLabelColor } }
#endif

/// The editor's text surface, sized to its content so the colour gutter can
/// align row-for-row (scrolling belongs to the enclosing ScrollView).
struct YAMLTextView: View {
    @Binding var text: String
    /// Insertion point, so the accessory bar types where the cursor is.
    @Binding var cursor: Int
    var accent: Color

    var body: some View {
        YAMLTextViewRepresentable(text: $text, cursor: $cursor, accent: accent)
            .frame(height: max(YAMLFont.lineHeight * 6,
                               CGFloat(text.components(separatedBy: "\n").count) * YAMLFont.lineHeight
                                   + YAMLFont.insets * 2))
    }
}

// MARK: - iOS

#if os(iOS)
struct YAMLTextViewRepresentable: UIViewRepresentable {
    @Binding var text: String
    @Binding var cursor: Int
    var accent: Color

    func makeUIView(context: Context) -> UITextView {
        let view = UITextView()
        view.delegate = context.coordinator
        view.font = YAMLFont.platform
        view.backgroundColor = .clear
        view.isScrollEnabled = false
        view.textContainerInset = UIEdgeInsets(top: YAMLFont.insets, left: 0,
                                               bottom: YAMLFont.insets, right: 0)
        view.textContainer.lineFragmentPadding = 0
        // The whole reason this view exists (§3.6 note 1).
        view.autocorrectionType = .no
        view.autocapitalizationType = .none
        view.smartQuotesType = .no
        view.smartDashesType = .no
        view.smartInsertDeleteType = .no
        view.spellCheckingType = .no
        view.text = text
        return view
    }

    func updateUIView(_ view: UITextView, context: Context) {
        guard view.text != text else { return }
        let selected = view.selectedRange
        view.attributedText = YAMLHighlight.attributed(
            text, accent: UIColor(accent), label: .label)
        view.selectedRange = NSRange(location: min(selected.location, (text as NSString).length),
                                     length: 0)
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, UITextViewDelegate {
        private let parent: YAMLTextViewRepresentable
        init(_ parent: YAMLTextViewRepresentable) { self.parent = parent }

        func textViewDidChange(_ textView: UITextView) {
            parent.text = textView.text
            parent.cursor = textView.selectedRange.location
        }

        func textViewDidChangeSelection(_ textView: UITextView) {
            parent.cursor = textView.selectedRange.location
        }
    }
}
#endif

// MARK: - macOS

#if os(macOS)
struct YAMLTextViewRepresentable: NSViewRepresentable {
    @Binding var text: String
    @Binding var cursor: Int
    var accent: Color

    func makeNSView(context: Context) -> NSTextView {
        let view = NSTextView()
        view.delegate = context.coordinator
        view.font = YAMLFont.platform
        view.drawsBackground = false
        view.isRichText = false
        view.textContainerInset = NSSize(width: 0, height: YAMLFont.insets)
        view.textContainer?.lineFragmentPadding = 0
        // §3.6 note 1 — AppKit is the worse offender: quote and dash
        // substitution are ON by default and will silently break YAML.
        view.isAutomaticQuoteSubstitutionEnabled = false
        view.isAutomaticDashSubstitutionEnabled = false
        view.isAutomaticTextReplacementEnabled = false
        view.isAutomaticSpellingCorrectionEnabled = false
        view.isContinuousSpellCheckingEnabled = false
        view.isGrammarCheckingEnabled = false
        view.isVerticallyResizable = false
        view.isHorizontallyResizable = false
        view.autoresizingMask = [.width]
        view.string = text
        return view
    }

    func updateNSView(_ view: NSTextView, context: Context) {
        guard view.string != text else { return }
        let selected = view.selectedRange()
        view.textStorage?.setAttributedString(
            YAMLHighlight.attributed(text, accent: NSColor(accent), label: .labelColor))
        view.setSelectedRange(NSRange(location: min(selected.location, (text as NSString).length),
                                      length: 0))
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, NSTextViewDelegate {
        private let parent: YAMLTextViewRepresentable
        init(_ parent: YAMLTextViewRepresentable) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let view = notification.object as? NSTextView else { return }
            parent.text = view.string
            parent.cursor = view.selectedRange().location
        }

        func textViewDidChangeSelection(_ notification: Notification) {
            guard let view = notification.object as? NSTextView else { return }
            parent.cursor = view.selectedRange().location
        }
    }
}
#endif
