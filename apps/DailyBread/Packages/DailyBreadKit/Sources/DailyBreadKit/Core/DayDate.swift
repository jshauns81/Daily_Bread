import Foundation

/// A calendar date with no time component — mirrors the API's DateOnly,
/// serialized as "yyyy-MM-dd". The SERVER owns "today" (family timezone);
/// clients pass explicit dates and never guess across the day boundary.
public struct DayDate: Hashable, Comparable, Sendable {
    public var year: Int
    public var month: Int
    public var day: Int

    public init(year: Int, month: Int, day: Int) {
        self.year = year
        self.month = month
        self.day = day
    }

    public static func < (lhs: DayDate, rhs: DayDate) -> Bool {
        (lhs.year, lhs.month, lhs.day) < (rhs.year, rhs.month, rhs.day)
    }

    /// The device's local calendar date. Prefer server-provided dates for
    /// anything that matters; this exists for UI conveniences only.
    public static func todayLocal() -> DayDate {
        let parts = Calendar.current.dateComponents([.year, .month, .day], from: Date())
        return DayDate(year: parts.year ?? 2000, month: parts.month ?? 1, day: parts.day ?? 1)
    }

    public var wireString: String {
        String(format: "%04d-%02d-%02d", year, month, day)
    }

    public init?(wireString: String) {
        let parts = wireString.split(separator: "-")
        guard parts.count == 3,
              let y = Int(parts[0]), let m = Int(parts[1]), let d = Int(parts[2]),
              (1...12).contains(m), (1...31).contains(d) else { return nil }
        self.init(year: y, month: m, day: d)
    }

    /// Midday Date in the current calendar — safe for display formatting.
    public var displayDate: Date {
        var parts = DateComponents()
        parts.year = year
        parts.month = month
        parts.day = day
        parts.hour = 12
        return Calendar.current.date(from: parts) ?? Date()
    }

    /// "Saturday, July 19"
    public var longDisplay: String {
        displayDate.formatted(.dateTime.weekday(.wide).month(.wide).day())
    }

    /// "Jul 19"
    public var shortDisplay: String {
        displayDate.formatted(.dateTime.month(.abbreviated).day())
    }
}

extension DayDate: Codable {
    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        let raw = try container.decode(String.self)
        guard let value = DayDate(wireString: raw) else {
            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unparseable date: \(raw)")
        }
        self = value
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(wireString)
    }
}

/// Timestamps from the API arrive as ISO-8601, sometimes with .NET's
/// 7-digit fractional seconds and sometimes without a timezone suffix.
/// Decode leniently; treat suffix-less values as UTC.
public struct LenientDate: Hashable, Codable, Sendable {
    public var date: Date

    public init(_ date: Date) {
        self.date = date
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        let raw = try container.decode(String.self)
        guard let parsed = Self.parse(raw) else {
            throw DecodingError.dataCorruptedError(
                in: container,
                debugDescription: "Unparseable timestamp: \(raw)")
        }
        date = parsed
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(date.formatted(.iso8601))
    }

    public static func parse(_ raw: String) -> Date? {
        // Normalize: cap fractional seconds at 3 digits, default to UTC.
        var s = raw
        if let dotIndex = s.firstIndex(of: ".") {
            let afterDot = s.index(after: dotIndex)
            var fractionEnd = afterDot
            while fractionEnd < s.endIndex, s[fractionEnd].isNumber {
                fractionEnd = s.index(after: fractionEnd)
            }
            let fraction = s[afterDot..<fractionEnd].prefix(3)
            s.replaceSubrange(dotIndex..<fractionEnd, with: ".\(fraction)")
        }
        let hasZone = s.hasSuffix("Z") || s.range(of: #"[+-]\d\d:\d\d$"#, options: .regularExpression) != nil
        if !hasZone { s += "Z" }

        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = withFraction.date(from: s) { return d }

        let plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
        return plain.date(from: s)
    }
}
