import SwiftUI
import DailyBreadKit

/// Parent sheet: tune ONE child's screen-time settings — but told in plain
/// language, not raw math. A starting-point preset sets a sensible bundle;
/// three smart dials (how much screen time, how firm the consequences, the
/// weekly allowance) each stand in for several underlying numbers at once; and
/// an "Expert" reveal opens the exact values for anyone who wants them. The raw
/// settings are the single source of truth — the smart dials read and write
/// them through pure mappings, so nothing can drift out of sync, and whatever
/// you see is exactly what saves. Prefills from the child's current summary;
/// saving PUTs the settings and hands the fresh summary back through `onSaved`.
struct ScreenTimeSettingsSheet: View {
    let childUserId: String
    let childName: String
    var onSaved: (ScreenTimeSummary) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    // Canonical raw settings — the exact numbers that get saved.
    @State private var weekdayHours: Double
    @State private var weekendHours: Double
    @State private var weekdayPercent: Double
    @State private var weekendPercent: Double
    @State private var minutesPerPoint: Double
    @State private var payoutText: String
    @State private var birthday: Date?

    @State private var expertOpen = false
    @State private var saving = false
    @State private var errorMessage: String?

    init(childUserId: String,
         childName: String,
         summary: ScreenTimeSummary,
         onSaved: @escaping (ScreenTimeSummary) -> Void) {
        self.childUserId = childUserId
        self.childName = childName
        self.onSaved = onSaved
        _weekdayHours = State(initialValue: Double(summary.weekdayPool.baseMinutes) / 60)
        _weekendHours = State(initialValue: Double(summary.weekendPool.baseMinutes) / 60)
        _weekdayPercent = State(initialValue: Double(Self.percent(of: summary.weekdayPool, default: 30)))
        _weekendPercent = State(initialValue: Double(Self.percent(of: summary.weekendPool, default: 20)))
        _minutesPerPoint = State(initialValue: Double(max(1, summary.minutesPerImportancePoint)))
        _payoutText = State(initialValue: summary.weeklyRoutinePayout.wireString)
        _birthday = State(initialValue: summary.birthDate?.displayDate)
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
                    startingPointCard
                    screenTimeCard
                    consequencesCard
                    allowanceCard
                    aboutCard
                    expertCard

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
        .frame(minWidth: 460, idealWidth: 500, minHeight: 600, idealHeight: 660)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    // MARK: - Starting point (presets)

    private var startingPointCard: some View {
        SheetCard(title: "Starting point") {
            HStack(spacing: 8) {
                ForEach(SmartTuning.presets) { preset in
                    let on = activePreset == preset.name
                    Button {
                        Haptics.tick()
                        withAnimation(.easeInOut(duration: 0.2)) { apply(preset) }
                    } label: {
                        Text(preset.name)
                            .font(.subheadline.weight(.semibold))
                            .frame(maxWidth: .infinity, minHeight: 38)
                            .foregroundStyle(on ? Color.white : Color.secondary)
                            .background(on ? Color.accentColor : Color.secondary.opacity(0.14),
                                        in: RoundedRectangle(cornerRadius: 11, style: .continuous))
                    }
                    .buttonStyle(.plain)
                }
            }
            Text(activePreset == nil
                 ? "Custom — you've fine-tuned this from a preset."
                 : "Sets everything below to a sensible bundle — nudge it from there.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Screen time dial

    private var screenTimeCard: some View {
        SheetCard(title: "Screen time") {
            SheetField(
                label: "Each day",
                value: hoursPerDayLabel,
                valueColor: Color.accentColor) {
                Slider(value: dailyBinding, in: 2...12, step: 0.5)
            }
            Toggle(isOn: weekendBonusBinding) {
                Text("Weekends get a little more")
                    .font(.subheadline)
            }
            Text("Pools: **\(hoursLabel(weekdayHours))** across weekdays, **\(hoursLabel(weekendHours))** across the weekend.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Consequences dial

    private var consequencesCard: some View {
        SheetCard(title: "Consequences") {
            SheetField(
                label: "How firm",
                value: SmartTuning.word(forConsequence: consequenceBinding.wrappedValue),
                valueColor: Color.accentColor) {
                Slider(value: consequenceBinding, in: 0...1)
                HStack {
                    Text("Gentle").font(.caption2).foregroundStyle(.tertiary)
                    Spacer()
                    Text("Strict").font(.caption2).foregroundStyle(.tertiary)
                }
            }
            Text("One missed chore costs up to **\(ScreenTimeFormat.minutes(perChoreMaxMinutes))**. A rough week can cost at most **~\(worstWeekLabel)** of the weekly pool — the rest is always \(childName)'s.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Allowance

    private var allowanceCard: some View {
        SheetCard(title: "Routine allowance") {
            HStack(spacing: 6) {
                Text("$")
                    .foregroundStyle(DB.gold(scheme))
                    .font(.body.weight(.semibold))
                TextField("10.00", text: $payoutText)
                    .textFieldStyle(.plain)
                    #if os(iOS)
                    .keyboardType(.decimalPad)
                    #endif
                Text("/ week")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .sheetFieldBackground()
            Text("What \(childName) earns for keeping the weekly routine.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Expert (exact numbers)

    // MARK: - About (birthday → age-appropriate voice)

    private var aboutCard: some View {
        SheetCard(title: "About \(childName)") {
            if let picked = birthday {
                DatePicker(
                    "Birthday",
                    selection: Binding(get: { birthday ?? picked }, set: { birthday = $0 }),
                    in: ...Date(),
                    displayedComponents: .date)
                    #if os(iOS)
                    .datePickerStyle(.compact)
                    #endif
                Text("The app matches its wording to \(childName)'s age.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Button {
                    // Start the picker somewhere sensible rather than today.
                    birthday = Calendar.current.date(byAdding: .year, value: -13, to: Date())
                } label: {
                    Label("Add \(childName)'s birthday", systemImage: "gift")
                        .font(.subheadline.weight(.medium))
                }
                Text("Optional — lets the app speak to \(childName) in an age-appropriate way.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    /// Device-local Y/M/D for the birthday — a plain calendar date, no time.
    private static func dayDate(from date: Date) -> DayDate {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: date)
        return DayDate(year: c.year ?? 2000, month: c.month ?? 1, day: c.day ?? 1)
    }

    private var expertCard: some View {
        SheetCard {
            DisclosureGroup(isExpanded: $expertOpen) {
                VStack(spacing: 14) {
                    SheetField(label: "Weekday pool",
                               value: hoursLabel(weekdayHours), valueColor: Color.accentColor) {
                        Slider(value: $weekdayHours, in: 0...60, step: 0.5)
                    }
                    SheetField(label: "Weekend pool",
                               value: hoursLabel(weekendHours), valueColor: Color.accentColor) {
                        Slider(value: $weekendHours, in: 0...40, step: 0.5)
                    }
                    SheetField(label: "Weekday — up to \(Int(weekdayPercent))% can be lost") {
                        Slider(value: $weekdayPercent, in: 0...100, step: 5)
                    }
                    SheetField(label: "Weekend — up to \(Int(weekendPercent))% can be lost") {
                        Slider(value: $weekendPercent, in: 0...100, step: 5)
                    }
                    SheetField(label: "Minutes per importance point",
                               value: "\(Int(minutesPerPoint)) min", valueColor: Color.accentColor) {
                        Slider(value: $minutesPerPoint, in: 1...30, step: 1)
                    }
                    Text("These are the exact numbers the dials above are set to. Change them here any time — the dials will simply read \"Custom.\"")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                        .padding(.top, 2)
                }
                .padding(.top, 12)
            } label: {
                Text("Fine-tune the exact numbers")
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(.primary)
            }
            .tint(Color.accentColor)
        }
    }

    // MARK: - Smart bindings (raw is canonical; these read/write it)

    /// Screen time per day, derived from the weekday pool (5 school days).
    private var dailyBinding: Binding<Double> {
        Binding(
            get: { weekdayHours / 5 },
            set: { day in
                let pools = SmartTuning.pools(daily: day, weekendBonus: weekendBonusOn)
                weekdayHours = pools.weekday
                weekendHours = pools.weekend
            })
    }

    /// Whether the weekend runs richer than a weekday — read back from the pools.
    private var weekendBonusOn: Bool {
        let daily = weekdayHours / 5
        guard daily > 0 else { return false }
        return (weekendHours / 2) > daily * 1.1
    }

    private var weekendBonusBinding: Binding<Bool> {
        Binding(
            get: { weekendBonusOn },
            set: { on in
                weekendHours = SmartTuning.pools(daily: weekdayHours / 5, weekendBonus: on).weekend
            })
    }

    /// Firmness (0 = gentle, 1 = strict), standing in for the price rate and
    /// both at-risk percentages at once.
    private var consequenceBinding: Binding<Double> {
        Binding(
            get: { SmartTuning.consequence(forRate: minutesPerPoint) },
            set: { level in
                let p = SmartTuning.params(forConsequence: level)
                minutesPerPoint = p.rate.rounded()
                weekdayPercent = (p.weekdayPct / 5).rounded() * 5
                weekendPercent = (p.weekendPct / 5).rounded() * 5
            })
    }

    // MARK: - Derived labels

    private var hoursPerDayLabel: String {
        let perDay = weekdayHours / 5
        return "\(hoursLabel(perDay)) / day"
    }

    /// "8h" / "7h 30m" / "0m" from a Double hours value.
    private func hoursLabel(_ hours: Double) -> String {
        ScreenTimeFormat.minutes(Int((hours * 60).rounded()))
    }

    private var perChoreMaxMinutes: Int {
        Int((10 * minutesPerPoint).rounded())
    }

    private var worstWeekLabel: String {
        let hours = weekdayHours * weekdayPercent / 100
        return hoursLabel(hours)
    }

    /// The preset whose bundle exactly matches the current raw values, if any.
    private var activePreset: String? {
        SmartTuning.presets.first { preset in
            let raw = SmartTuning.raw(for: preset)
            return abs(raw.weekday - weekdayHours) < 0.5
                && abs(raw.weekend - weekendHours) < 0.5
                && abs(raw.rate - minutesPerPoint) < 0.5
                && abs(raw.weekdayPct - weekdayPercent) < 2.5
                && abs(raw.weekendPct - weekendPercent) < 2.5
        }?.name
    }

    private func apply(_ preset: SmartTuning.Preset) {
        let raw = SmartTuning.raw(for: preset)
        weekdayHours = raw.weekday
        weekendHours = raw.weekend
        minutesPerPoint = raw.rate
        weekdayPercent = raw.weekdayPct
        weekendPercent = raw.weekendPct
    }

    // MARK: - Save

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
            weekendAtRiskPercent: Int(weekendPercent),
            minutesPerImportancePoint: Int(minutesPerPoint),
            birthDate: birthday.map(Self.dayDate(from:)))

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

/// The pure mappings behind the smart dials. Kept free of SwiftUI so the
/// preset bundles and the dial ↔ raw translations are simple to reason about
/// (and to test): three anchor points, linearly interpolated.
enum SmartTuning {
    struct Preset: Identifiable {
        let name: String
        let daily: Double
        let weekendBonus: Bool
        let consequence: Double
        var id: String { name }
    }

    static let presets: [Preset] = [
        Preset(name: "Relaxed", daily: 10, weekendBonus: true, consequence: 0.2),
        Preset(name: "Balanced", daily: 8, weekendBonus: true, consequence: 0.5),
        Preset(name: "Structured", daily: 6, weekendBonus: false, consequence: 0.85),
    ]

    /// Screen-time dial (hours/day) + weekend bonus → weekly pool hours.
    static func pools(daily: Double, weekendBonus: Bool) -> (weekday: Double, weekend: Double) {
        let weekday = min(60, round2(daily * 5))
        let weekendPerDay = weekendBonus ? daily * 1.25 : daily
        let weekend = min(40, round2(weekendPerDay * 2))
        return (weekday, weekend)
    }

    /// Consequence dial (0…1) → price rate + both at-risk percents.
    /// Anchors: gentle(0) → (3, 15, 10); balanced(0.5) → (6, 30, 20); strict(1) → (10, 45, 30).
    static func params(forConsequence c: Double) -> (rate: Double, weekdayPct: Double, weekendPct: Double) {
        let c = min(1, max(0, c))
        func lerp(_ a: Double, _ b: Double, _ t: Double) -> Double { a + (b - a) * t }
        if c <= 0.5 {
            let t = c / 0.5
            return (lerp(3, 6, t), lerp(15, 30, t), lerp(10, 20, t))
        } else {
            let t = (c - 0.5) / 0.5
            return (lerp(6, 10, t), lerp(30, 45, t), lerp(20, 30, t))
        }
    }

    /// Read a dial position back from the price rate (the primary firmness lever).
    static func consequence(forRate rate: Double) -> Double {
        let clamped = min(10, max(3, rate))
        if clamped <= 6 { return ((clamped - 3) / 3) * 0.5 }
        return 0.5 + ((clamped - 6) / 4) * 0.5
    }

    static func word(forConsequence c: Double) -> String {
        switch c {
        case ..<0.34: return "Gentle"
        case ..<0.67: return "Balanced"
        default: return "Strict"
        }
    }

    /// A preset's full raw settings, rounded the way the sliders round them.
    static func raw(for preset: Preset) -> (weekday: Double, weekend: Double, rate: Double, weekdayPct: Double, weekendPct: Double) {
        let pl = pools(daily: preset.daily, weekendBonus: preset.weekendBonus)
        let pr = params(forConsequence: preset.consequence)
        return (pl.weekday,
                pl.weekend,
                pr.rate.rounded(),
                (pr.weekdayPct / 5).rounded() * 5,
                (pr.weekendPct / 5).rounded() * 5)
    }

    private static func round2(_ x: Double) -> Double { (x * 2).rounded() / 2 }
}
