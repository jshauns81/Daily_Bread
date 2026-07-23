import Foundation

/// The signed-in child's age tier, mirrored from the server (which owns the
/// calendar, so the tier never drifts on a device). Two tiers only: a Teen
/// (13+) is never spoken to like a little kid. An unknown tier reads as younger.
public enum AgeTier: String, Sendable {
    case younger
    case teen

    public init(wire: String?) {
        self = (wire == AgeTier.teen.rawValue) ? .teen : .younger
    }

    public var isTeen: Bool { self == .teen }
}

/// Age-appropriate word choices for kid-facing copy. Pick phrases from here
/// instead of hardcoding "a grown-up" so the app grows up alongside the child.
public struct KidVoice: Sendable {
    public let tier: AgeTier

    public init(_ tier: AgeTier) { self.tier = tier }

    public var isTeen: Bool { tier.isTeen }

    /// Plural / collective: "your parents" (teen) vs "a grown-up" (younger).
    /// Reads naturally after "waiting on".
    public var parents: String { isTeen ? "your parents" : "a grown-up" }

    /// Singular actor: "a parent" (teen) vs "a grown-up" (younger).
    /// Reads naturally after "until … responds".
    public var parent: String { isTeen ? "a parent" : "a grown-up" }
}
