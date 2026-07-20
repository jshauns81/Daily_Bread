import SwiftUI
import DailyBreadKit

/// Parent sheet: tune ONE child's screen-time settings — weekly allowances,
/// how much of each pool can be lost, and the weekly routine payout. Built from
/// explicit cards (not a macOS `Form`, which renders cramped) so it reads clean
/// on iOS and macOS. Prefills from the child's current summary; saving PUTs the
/// settings and hands the fresh summary back through `onSaved`. Errors show
/// inline — never a system alert.
struct ScreenTimeSettingsSheet: View {
    let childUserId: String
    let childName: String
    var onSaved: (ScreenTimeSummary) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var weekdayHours: Double
    @State private var weekendHours: Double
    @State private var weekdayPercent: Double
    @State private var weekendPercent: Double
    @State private var payoutText: String
    @State private var saving = false
    @State private var errorMessage: String?

    init(childUserId: String,
         childName: String,
         summary: ScreenTimeSummary,
         currentPayout: Money? = nil,
         onSaved: @escaping (ScreenTimeSummary) -> Void) {
        self.childUserId = childUserId
        self.childName = childName
        self.onSaved = onSaved
        _weekdayHours = State(initialValue: Double(summary.weekdayPool.baseMinutes) / 60)
        _weekendHours = State(initialValue: Double(summary.weekendPool.baseMinutes) / 60)
        _weekdayPercent = State(initialValue: Double(Self.percent(of: summary.weekdayPool, default: 30)))
        _weekendPercent = State(initialValue: Double(Self.percent(of: summary.weekendPool, default: 50)))
        _payoutText = State(initialValue: currentPayout?.wireString ?? "10.00")
    }

    /// atRisk as a percent of base, guarded against an empty pool.
    private static func percent(of pool: ScreenTimePool, default fallback: Int) -> Int {
        guard pool.baseMinutes > 0 else { return fallback }
        let raw = (Double(pool.atRiskMinutes) * 100 / Double(pool.baseMinutes)).rounded()
        return min(100, max(0, Int(raw)))
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "\(childName)'s Screen Time")

            ScrollView {
                VStack(spacing: 14) {
                    poolCard(title: "Weekdays · Mon–Fri",
                             hours: $weekdayHours, hoursRange: 0...60,
                             percent: $weekdayPercent)
                    poolCard(title: "Weekend · Sat–Sun",
                             hours: $weekendHours, hoursRange: 0...40,
                             percent: $weekendPercent)

                    SheetCard(title: "Weekly routine payout") {
                        HStack(spacing: 6) {
                            Text("$")
                                .foregroundStyle(DB.gold(scheme))
                                .font(.body.weight(.semibold))
                            TextField("10.00", text: $payoutText)
                                .textFieldStyle(.plain)
                                #if os(iOS)
                                .keyboardType(.decimalPad)
                                #endif
                        }
                        .sheetFieldBackground()
                        Text("What \(childName) earns for keeping the weekly routine.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }

                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote)
                            .foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal)
                .padding(.top, 4)
                .padding(.bottom, 12)
            }

            SheetActionBar(
                saveTitle: "Save",
                saving: saving,
                canSave: payoutDecimal != nil,
                onCancel: { dismiss() },
                onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 460, idealWidth: 500, minHeight: 540, idealHeight: 580)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    private func poolCard(title: String,
                          hours: Binding<Double>,
                          hoursRange: ClosedRange<Double>,
                          percent: Binding<Double>) -> some View {
        SheetCard(title: title) {
            SheetField(
                label: "Allowance",
                value: ScreenTimeFormat.minutes(Int(hours.wrappedValue * 60)),
                valueColor: Color.accentColor) {
                Slider(value: hours, in: hoursRange, step: 0.5)
            }
            SheetField(label: "Up to \(Int(percent.wrappedValue))% can be lost") {
                Slider(value: percent, in: 0...100, step: 5)
            }
        }
    }

    /// The payout field parsed to a non-negative Decimal, or nil if invalid.
    private var payoutDecimal: Decimal? {
        let trimmed = payoutText
            .trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: ",", with: ".")
        guard !trimmed.isEmpty,
              let value = Decimal(string: trimmed, locale: Locale(identifier: "en_US_POSIX")),
              value >= 0 else { return nil }
        return value
    }

    private func save() async {
        guard let payout = payoutDecimal else {
            errorMessage = "Enter a valid payout amount."
            return
        }
        saving = true
        defer { saving = false }
        errorMessage = nil

        let update = ScreenTimeSettingsUpdate(
            userId: childUserId,
            weekdayHours: weekdayHours,
            weekendHours: weekendHours,
            weeklyRoutinePayout: Money(payout),
            weekdayAtRiskPercent: Int(weekdayPercent),
            weekendAtRiskPercent: Int(weekendPercent))

        do {
            let fresh = try await session.client.updateScreenTimeSettings(update)
            Haptics.success()
            onSaved(fresh)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
