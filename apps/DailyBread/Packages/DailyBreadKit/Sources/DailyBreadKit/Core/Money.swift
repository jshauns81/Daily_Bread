import Foundation

/// Money on the wire is a decimal STRING ("12.50") per the API contract, so
/// values decode straight into Decimal without a double round-trip.
/// Accepts a bare JSON number too, for robustness.
public struct Money: Hashable, Sendable {
    public var amount: Decimal

    public init(_ amount: Decimal) {
        self.amount = amount
    }

    public static let zero = Money(0)

    public var isZero: Bool { amount == 0 }
    public var isNegative: Bool { amount < 0 }
}

extension Money: Codable {
    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let string = try? container.decode(String.self) {
            guard let value = Decimal(string: string, locale: Locale(identifier: "en_US_POSIX")) else {
                throw DecodingError.dataCorruptedError(
                    in: container,
                    debugDescription: "Unparseable money string: \(string)")
            }
            amount = value
        } else {
            // Fallback: bare number. Convert via string to dodge Double artifacts.
            let double = try container.decode(Double.self)
            amount = Decimal(string: String(double), locale: Locale(identifier: "en_US_POSIX")) ?? Decimal(double)
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(wireString)
    }

    /// Two-decimal wire representation ("12.50").
    public var wireString: String {
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.numberStyle = .decimal
        formatter.minimumFractionDigits = 2
        formatter.maximumFractionDigits = 2
        formatter.usesGroupingSeparator = false
        return formatter.string(from: amount as NSDecimalNumber) ?? "0.00"
    }
}

public extension Money {
    /// "$12.50" for display. Currency display is USD by design (family app).
    var display: String {
        let formatter = NumberFormatter()
        formatter.numberStyle = .currency
        formatter.currencyCode = "USD"
        formatter.maximumFractionDigits = 2
        formatter.minimumFractionDigits = 2
        return formatter.string(from: amount as NSDecimalNumber) ?? "$\(wireString)"
    }

    /// "+$2.00" / "−$5.00" for ledger rows.
    var signedDisplay: String {
        isNegative ? "−\(Money(-amount).display)" : "+\(display)"
    }
}
