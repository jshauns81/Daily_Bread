import SwiftUI
import DailyBreadKit

/// Parent sheet: tune ONE child's screen-time settings — weekly allowances,
/// how much of each pool can be lost, and the weekly routine payout.
/// Prefills from the child's current summary; saving PUTs the settings and
/// hands the fresh summary back through `onSaved`. Errors show inline
/// (DB.help) — never a system alert.
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
        NavigationStack {
            Form {
                Section {
                    hoursSlider("Weekday allowance", tag: "Mon–Fri",
                                value: $weekdayHours, range: 0...60)
                    percentSlider(value: $weekdayPercent)
                } header: {
                    Text("Weekdays")
                }

                Section {
                    hoursSlider("Weekend allowance", tag: "Sat–Sun",
                                value: $weekendHours, range: 0...40)
                    percentSlider(value: $weekendPercent)
                } header: {
                    Text("Weekend")
                }

                Section {
                    HStack {
                        Text("$")
                            .foregroundStyle(DB.gold(scheme))
                        TextField("10.00", text: $payoutText)
                            #if os(iOS)
                            .keyboardType(.decimalPad)
                            #endif
                    }
                } header: {
                    Text("Weekly routine payout")
                } footer: {
                    Text("What \(childName) earns for keeping the weekly routine.")
                }

                Section {
                    Button {
                        Task { await save() }
                    } label: {
                        if saving {
                            ProgressView()
                                .frame(maxWidth: .infinity)
                        } else {
                            Text("Save")
                                .font(.body.weight(.semibold))
                                .frame(maxWidth: .infinity)
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Color.accentColor)
                    .disabled(saving || payoutDecimal == nil)
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())

                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote)
                            .foregroundStyle(DB.help(scheme))
                            .listRowBackground(Color.clear)
                    }
                }
            }
            .navigationTitle("\(childName)'s Screen Time")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                        .disabled(saving)
                }
            }
        }
        .graphiteBackground()
    }

    private func hoursSlider(_ title: String, tag: String,
                             value: Binding<Double>,
                             range: ClosedRange<Double>) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                    .font(.subheadline.weight(.medium))
                Spacer()
                Text(tag)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
                Text(ScreenTimeFormat.minutes(Int(value.wrappedValue * 60)))
                    .font(.subheadline.weight(.bold))
                    .foregroundStyle(Color.accentColor)
                    .monospacedDigit()
            }
            Slider(value: value, in: range, step: 0.5)
        }
        .padding(.vertical, 2)
    }

    private func percentSlider(value: Binding<Double>) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Up to \(Int(value.wrappedValue))% can be lost")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .monospacedDigit()
            Slider(value: value, in: 0...100, step: 5)
        }
        .padding(.vertical, 2)
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
