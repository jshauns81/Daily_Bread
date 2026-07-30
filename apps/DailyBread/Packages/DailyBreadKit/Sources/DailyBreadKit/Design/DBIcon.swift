import SwiftUI

/// The size an icon renders at, tied to the type ramp so icons scale with Dynamic Type
/// exactly like the text they sit beside.
public enum DBIconSize: Sendable {
    /// Small inline — chips, captions, badge glyphs.
    case caption
    /// Default — inline with body text, list rows, buttons.
    case body
    /// Section headers, toolbar chrome, stat tiles.
    case title
    /// Hero moments and empty states.
    case display

    var font: Font {
        switch self {
        case .caption: return .caption
        case .body: return .body
        case .title: return .title3
        case .display: return .largeTitle
        }
    }
}

/// The one view every icon goes through. SF Symbols stays the icon set (evaluated against
/// Phosphor/Iconoir/Lucide and kept — symbolEffect, Dynamic Type and Label alignment come
/// free); this wrapper exists so sizing stops drifting per call site and so a future
/// `icons.set` theme key has somewhere to land.
public struct DBIcon: View {
    public let name: String            // SF Symbol name
    public var weight: Font.Weight
    public var tint: Color?            // nil = inherit from context
    public var size: DBIconSize

    public init(_ name: String,
                weight: Font.Weight = .medium,
                tint: Color? = nil,
                size: DBIconSize = .body) {
        self.name = name
        self.weight = weight
        self.tint = tint
        self.size = size
    }

    public var body: some View {
        if let tint {
            image.foregroundStyle(tint)
        } else {
            image
        }
    }

    private var image: some View {
        Image(systemName: name)
            .font(size.font.weight(weight))
    }
}

#Preview("Sizes and tints") {
    VStack(alignment: .leading, spacing: 16) {
        HStack(spacing: 14) {
            DBIcon("checkmark.circle.fill", size: .caption)
            DBIcon("checkmark.circle.fill")
            DBIcon("checkmark.circle.fill", size: .title)
            DBIcon("checkmark.circle.fill", size: .display)
        }
        HStack(spacing: 14) {
            DBIcon("car.fill", tint: DB.night(.light))
            DBIcon("hand.raised.fill", tint: DB.help(.light))
            DBIcon("dollarsign.circle.fill", tint: DB.gold(.light))
            DBIcon("checkmark.seal.fill", tint: DB.success(.light))
        }
        Label { Text("Inherits inside Label") } icon: { DBIcon("moon.stars.fill") }
    }
    .padding()
}
