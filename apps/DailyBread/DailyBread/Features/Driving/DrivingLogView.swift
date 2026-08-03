import SwiftUI
import DailyBreadKit

enum DrivingLogMode { case kid, parent }

@MainActor
@Observable
final class DrivingLogStore {
    var entries: [DrivingLogEntry] = []
    var progress: DrivingLogProgress?
    var loading = false
    var errorMessage: String?
    var busy = false

    func load(_ session: SessionStore, mode: DrivingLogMode) async {
        loading = entries.isEmpty
        defer { loading = false }
        do {
            if mode == .parent {
                entries = try await session.client.pendingDrivingEntries()
            } else {
                async let e = session.client.drivingEntries()
                async let p = session.client.drivingProgress()
                entries = try await e
                progress = try await p
            }
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func approve(_ entry: DrivingLogEntry, _ session: SessionStore) async {
        await mutate(session) { try await session.client.approveDrivingEntry(id: entry.id) }
    }

    func reject(_ entry: DrivingLogEntry, reason: String?, _ session: SessionStore) async {
        let t = reason?.trimmingCharacters(in: .whitespacesAndNewlines)
        await mutate(session) {
            try await session.client.rejectDrivingEntry(id: entry.id, reason: (t?.isEmpty ?? true) ? nil : t)
        }
    }

    private func mutate(_ session: SessionStore, _ action: @Sendable () async throws -> Void) async {
        busy = true
        defer { busy = false }
        do {
            try await action()
            Haptics.success()
            entries = try await session.client.pendingDrivingEntries()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}

struct DrivingLogView: View {
    var mode: DrivingLogMode

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = DrivingLogStore()
    @State private var logging = false
    @State private var rejecting: DrivingLogEntry?

    private var showChildName: Bool { session.children.count > 1 }

    var body: some View {
        List {
            if mode == .kid, let p = store.progress {
                Section { progressCard(p) }.listRowBackground(Color.clear)
            }

            if store.entries.isEmpty && !store.loading {
                Section { emptyState }.listRowBackground(Color.clear)
            } else {
                ForEach(store.entries) { entry in
                    Section { entryRow(entry) }
                }
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote).foregroundStyle(DB.help(scheme))
                }
            }
        }
        .navigationTitle(mode == .parent ? "Driving approvals" : "Driving log")
        .themeBackground()
        .toolbar {
            // §2.1: parents log drives too — auto-approved, stamped on the row.
            ToolbarItem(placement: .primaryAction) {
                Button { logging = true } label: { Image(systemName: "plus") }
            }
        }
        .sheet(isPresented: $logging) {
            DriveEditorSheet(asParent: mode == .parent) { await store.load(session, mode: mode) }
        }
        .sheet(item: $rejecting) { entry in
            DrivingRejectSheet(entry: entry) { reason in
                Task { await store.reject(entry, reason: reason, session) }
            }
        }
        .refreshable { await store.load(session, mode: mode) }
        .refreshOnForeground { await store.load(session, mode: mode) }
        .task { await store.load(session, mode: mode) }
    }

    private func progressCard(_ p: DrivingLogProgress) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            bar(title: "Total hours", value: p.totalHours, goal: p.totalGoalHours)
            bar(title: "Night hours", value: p.nightHours, goal: p.nightGoalHours)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 16)
    }

    private func bar(title: String, value: Double, goal: Double?) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title.uppercased())
                    .font(.caption.weight(.bold)).foregroundStyle(.secondary).kerning(0.8)
                Spacer()
                Text(goal != nil ? "\(hours(value)) / \(hours(goal!)) h" : "\(hours(value)) h")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Color.accentColor)
            }
            if let goal, goal > 0 {
                ProgressView(value: min(value, goal), total: goal)
                    .tint(Color.accentColor)
            }
        }
    }

    private func hours(_ v: Double) -> String {
        v == v.rounded() ? String(Int(v)) : String(format: "%.1f", v)
    }

    private func entryRow(_ entry: DrivingLogEntry) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Image(systemName: entry.isNightDriving ? "moon.stars.fill" : "car.fill")
                    .foregroundStyle(entry.isNightDriving ? DB.night(scheme) : Color.accentColor)
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(entry.date.shortDisplay) · \(entry.durationLabel)")
                        .font(.body.weight(.semibold))
                    Text(subtitle(entry))
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                statusBadge(entry)
            }

            if let notes = entry.routeNotes, !notes.isEmpty {
                Text(notes).font(.caption).foregroundStyle(.secondary)
            }
            if let reason = entry.rejectionReason, !reason.isEmpty, entry.isRejected {
                Text("“\(reason)”").font(.caption).italic().foregroundStyle(.secondary)
            }

            if mode == .parent && entry.isPending {
                HStack(spacing: 10) {
                    Button { rejecting = entry } label: {
                        Text("Decline").font(.subheadline.weight(.medium))
                            .frame(maxWidth: .infinity, minHeight: 38)
                    }
                    .buttonStyle(.plain)
                    .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .disabled(store.busy)

                    Button { Task { await store.approve(entry, session) } } label: {
                        Text("Approve").font(.subheadline.weight(.semibold))
                            .frame(maxWidth: .infinity, minHeight: 38).foregroundStyle(.white)
                    }
                    .buttonStyle(.plain)
                    .background(Color.accentColor, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .disabled(store.busy)
                }
            }
        }
        .padding(.vertical, 4)
    }

    private func subtitle(_ entry: DrivingLogEntry) -> String {
        var parts: [String] = []
        if showChildName && mode == .parent { parts.append(entry.childName) }
        parts.append("\(entry.startTime)–\(entry.endTime)")
        parts.append("with \(entry.supervisorLabel)")
        if entry.weather != "Clear" { parts.append(entry.weather.lowercased()) }
        // §2.1: a parent-logged drive says so — it skipped the approval queue.
        if entry.createdByParent {
            parts.append("logged by \(entry.decidedByLabel ?? "a parent")")
        }
        return parts.joined(separator: " · ")
    }

    @ViewBuilder
    private func statusBadge(_ entry: DrivingLogEntry) -> some View {
        if entry.isApproved {
            Image(systemName: "checkmark.seal.fill").foregroundStyle(DB.success(scheme))
        } else if entry.isRejected {
            Image(systemName: "xmark.circle").foregroundStyle(DB.help(scheme))
        } else {
            Text("Pending").font(.caption2.weight(.bold)).foregroundStyle(.secondary)
                .padding(.horizontal, 7).padding(.vertical, 3)
                .background(DB.fillOff(scheme), in: Capsule())
        }
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label(mode == .parent ? "Nothing waiting" : "No drives logged",
                  systemImage: mode == .parent ? "checkmark.circle" : "car")
        } description: {
            Text(mode == .parent
                 ? "Logged drives waiting for your approval show up here. You can also log one yourself with +."
                 : "Tap + to log a supervised drive. It counts toward your hours once a parent approves.")
        }
        .frame(maxWidth: .infinity).padding(.vertical, 20)
    }
}

// MARK: - §2.1 The duration-first drive editor

/// Log a supervised drive. The log exists to accumulate hours toward a licence,
/// so DURATION is the hero — date and exact times are one tap away, not three
/// stacked pickers. A parent logging on the child's behalf is auto-approved
/// server-side and stamped on the row.
private struct DriveEditorSheet: View {
    var asParent: Bool
    var onSaved: () async -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    private enum DateChoice: Equatable {
        case today, yesterday, picked(DayDate)

        var dayDate: DayDate {
            switch self {
            case .today: return DayDate.todayLocal()
            case .yesterday: return DayDate.todayLocal().addingDays(-1)
            case .picked(let d): return d
            }
        }
    }

    @State private var dateChoice: DateChoice = .today
    @State private var pickingDate = false
    @State private var durationMinutes = 30
    @State private var dragStartMinutes: Int?
    @State private var exactTimes = false
    @State private var start = Date()
    @State private var end = Date()
    @State private var childUserId: String?
    @State private var progress: DrivingLogProgress?
    @State private var supervisor = ""
    @State private var weather: DrivingWeather = .clear
    @State private var nightMode = 0   // 0 auto, 1 day, 2 night
    @State private var notes = ""
    @State private var saving = false
    @State private var errorMessage: String?

    private static let presets: [(label: String, minutes: Int)] = [
        ("15m", 15), ("30m", 30), ("45m", 45), ("1h", 60), ("1h 30m", 90), ("2h", 120)
    ]

    /// The single source of truth for how long the drive was.
    private var effectiveMinutes: Int {
        guard exactTimes else { return durationMinutes }
        let span = Calendar.current.dateComponents([.minute], from: start, to: end).minute ?? 0
        return span > 0 ? span : span + 24 * 60
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Log a drive")
            ScrollView {
                VStack(spacing: 14) {
                    if asParent && session.children.count > 1 {
                        SheetCard(title: "Who drove") { childChips }
                    }
                    SheetCard(title: "When") { dateChips }
                    SheetCard(title: "How long") { durationHero }
                    SheetCard(title: "Details") {
                        TextField("Supervising adult", text: $supervisor).sheetFieldBackground()
                        Picker("Weather", selection: $weather) {
                            ForEach(DrivingWeather.allCases) { Text($0.label).tag($0) }
                        }
                        .pickerStyle(.menu)
                        Picker("Night driving", selection: $nightMode) {
                            Text("Auto").tag(0); Text("Day").tag(1); Text("Night").tag(2)
                        }
                        .pickerStyle(.segmented)
                        TextField("Route notes (optional)", text: $notes, axis: .vertical)
                            .lineLimit(1...3).sheetFieldBackground()
                    }
                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote).foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal).padding(.top, 4).padding(.bottom, 12)
            }
            SheetActionBar(saveTitle: "Log it", saving: saving,
                           canSave: canSave,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
                .padding()
        }
        .themeBackground()
        .task { await prepare() }
        .sheet(isPresented: $pickingDate) {
            DrivenDatePickSheet(selected: dateChoice.dayDate) { picked in
                dateChoice = normalized(picked)
            }
        }
        #if os(macOS)
        .frame(minWidth: 440, idealWidth: 480, minHeight: 520, idealHeight: 580)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    private var canSave: Bool {
        !supervisor.trimmingCharacters(in: .whitespaces).isEmpty
            && effectiveMinutes > 0
            && (!asParent || childUserId != nil)
    }

    // MARK: Who

    private var childChips: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(session.children) { child in
                    chip(child.userName, selected: childUserId == child.userId) {
                        childUserId = child.userId
                        Task { await loadProgress() }
                    }
                }
            }
        }
    }

    // MARK: When

    private var dateChips: some View {
        HStack(spacing: 8) {
            chip("Today", selected: dateChoice == .today) { dateChoice = .today }
            chip("Yesterday", selected: dateChoice == .yesterday) { dateChoice = .yesterday }
            chip(pickedLabel, selected: isPicked) { pickingDate = true }
            Spacer(minLength: 0)
        }
    }

    private var isPicked: Bool {
        if case .picked = dateChoice { return true }
        return false
    }

    private var pickedLabel: String {
        if case .picked(let d) = dateChoice { return d.shortDisplay }
        return "Pick…"
    }

    /// A picked date that IS today/yesterday collapses onto its chip.
    private func normalized(_ d: DayDate) -> DateChoice {
        if d == DayDate.todayLocal() { return .today }
        if d == DayDate.todayLocal().addingDays(-1) { return .yesterday }
        return .picked(d)
    }

    // MARK: How long

    private var durationHero: some View {
        VStack(spacing: 12) {
            Text(durationLabel(effectiveMinutes))
                .font(.system(size: 44, weight: .heavy, design: .rounded))
                .foregroundStyle(Color.accentColor)
                .contentTransition(.numericText())
                .animation(.snappy, value: effectiveMinutes)
                .frame(maxWidth: .infinity)
                .gesture(exactTimes ? nil : dragToTune)
                .accessibilityLabel("Duration \(durationLabel(effectiveMinutes)). \(exactTimes ? "" : "Drag to fine-tune.")")

            if !exactTimes {
                HStack(spacing: 8) {
                    ForEach(Self.presets, id: \.minutes) { preset in
                        chip(preset.label, selected: durationMinutes == preset.minutes, compact: true) {
                            withAnimation(.snappy) { durationMinutes = preset.minutes }
                            Haptics.tick()
                        }
                    }
                }

                Text("drag the number to fine-tune")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            if let line = progressLine {
                Text(line)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText())
                    .animation(.snappy, value: effectiveMinutes)
            }

            // Exact times live behind a disclosure — present when needed, and
            // designed when present: themed fields, not a bare system stack.
            DisclosureGroup(isExpanded: $exactTimes) {
                VStack(spacing: 8) {
                    timeField("Start", $start)
                    timeField("End", $end)
                }
                .padding(.top, 8)
            } label: {
                Text("Set exact times")
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(Color.accentColor)
            }
        }
    }

    private var dragToTune: some Gesture {
        DragGesture()
            .onChanged { value in
                let base = dragStartMinutes ?? durationMinutes
                dragStartMinutes = base
                // ~18pt per 5-minute step; right = longer.
                let steps = Int(value.translation.width / 18)
                let tuned = base + steps * 5
                let clamped = min(8 * 60, max(5, tuned))
                if clamped != durationMinutes {
                    durationMinutes = clamped
                    Haptics.tick()
                }
            }
            .onEnded { _ in dragStartMinutes = nil }
    }

    private func timeField(_ label: String, _ binding: Binding<Date>) -> some View {
        HStack {
            Text(label)
                .font(.subheadline.weight(.medium))
            Spacer()
            DatePicker("", selection: binding, displayedComponents: .hourAndMinute)
                .labelsHidden()
        }
        .padding(.horizontal, 12).padding(.vertical, 8)
        .background(DB.fillOff(scheme), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    /// "brings you to 12.8 of 50 hours" — the whole reason the log exists.
    private var progressLine: String? {
        guard let p = progress else { return nil }
        let after = p.totalHours + Double(effectiveMinutes) / 60
        let afterLabel = String(format: after == after.rounded() ? "%.0f" : "%.1f", after)
        if let goal = p.totalGoalHours, goal > 0 {
            let goalLabel = String(format: goal == goal.rounded() ? "%.0f" : "%.1f", goal)
            return "brings \(asParent ? "them" : "you") to \(afterLabel) of \(goalLabel) hours"
        }
        return "brings \(asParent ? "them" : "you") to \(afterLabel) hours"
    }

    private func durationLabel(_ minutes: Int) -> String {
        let h = minutes / 60, m = minutes % 60
        if h > 0 && m > 0 { return "\(h)h \(m)m" }
        return h > 0 ? "\(h)h" : "\(m)m"
    }

    // MARK: Shared chip

    private func chip(_ label: String, selected: Bool, compact: Bool = false,
                      action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(label)
                .font(.subheadline.weight(selected ? .semibold : .regular))
                .padding(.horizontal, compact ? 10 : 14)
                .padding(.vertical, compact ? 6 : 8)
                .background(selected ? AnyShapeStyle(Color.accentColor.opacity(0.18))
                                     : AnyShapeStyle(DB.fillOff(scheme)),
                            in: Capsule())
                .overlay(Capsule().strokeBorder(
                    selected ? Color.accentColor : Color.clear, lineWidth: 1.5))
                .foregroundStyle(selected ? Color.accentColor : Color.primary)
        }
        .buttonStyle(.plain)
    }

    // MARK: Data

    private func prepare() async {
        if asParent {
            // Single child: no picker, target them directly (the invariant).
            if session.children.count == 1 { childUserId = session.children.first?.userId }
            // The logging parent supervised, until told otherwise.
            if supervisor.isEmpty { supervisor = session.currentUser?.userName ?? "" }
        }
        await loadProgress()
    }

    private func loadProgress() async {
        let target = asParent ? childUserId : nil
        guard !asParent || target != nil else { return }
        progress = try? await session.client.drivingProgress(userId: target)
    }

    private func hhmm(_ d: Date) -> String {
        let c = Calendar.current.dateComponents([.hour, .minute], from: d)
        return String(format: "%02d:%02d", c.hour ?? 0, c.minute ?? 0)
    }

    /// Duration-first logging still has to speak the wire's times. Anchor the
    /// end at "now" when the drive was today (people log right after they park)
    /// and late afternoon for a back-dated drive; night stays overridable.
    private func wireTimes() -> (start: String, end: String) {
        if exactTimes { return (hhmm(start), hhmm(end)) }
        let cal = Calendar.current
        let anchor: Date
        if dateChoice == .today {
            anchor = Date()
        } else {
            anchor = cal.date(bySettingHour: 17, minute: 0, second: 0, of: Date()) ?? Date()
        }
        let startDate = anchor.addingTimeInterval(-Double(effectiveMinutes) * 60)
        return (hhmm(startDate), hhmm(anchor))
    }

    private func save() async {
        let times = wireTimes()
        if times.start == times.end { errorMessage = "Start and end can't be the same."; return }
        saving = true
        defer { saving = false }
        errorMessage = nil
        let create = DrivingLogCreate(
            childUserId: asParent ? childUserId : nil,
            date: dateChoice.dayDate,
            startTime: times.start,
            endTime: times.end,
            nightOverride: nightMode == 0 ? nil : (nightMode == 2),
            supervisorName: supervisor.trimmingCharacters(in: .whitespaces),
            weather: weather.rawValue,
            routeNotes: notes.trimmingCharacters(in: .whitespaces).isEmpty ? nil : notes)
        do {
            _ = try await session.client.createDrivingEntry(create)
            Haptics.success()
            await onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}

// MARK: - Themed date pick (the "Pick…" chip)

/// A month grid in the calendar's own visual language — a designed surface,
/// not a bare system date sheet. Past days only; tapping picks and closes.
private struct DrivenDatePickSheet: View {
    var selected: DayDate
    var onPick: (DayDate) -> Void

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @State private var anchor = MonthMath.firstOfMonth(DayDate.todayLocal())

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 6), count: 7)
    private let weekdayLabels = ["S", "M", "T", "W", "T", "F", "S"]

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Which day")
            ScrollView {
                VStack(spacing: 14) {
                    monthHeader
                    HStack(spacing: 6) {
                        ForEach(Array(weekdayLabels.enumerated()), id: \.offset) { _, label in
                            Text(label)
                                .font(.caption2.weight(.bold))
                                .foregroundStyle(.secondary)
                                .frame(maxWidth: .infinity)
                        }
                    }
                    LazyVGrid(columns: columns, spacing: 6) {
                        ForEach(0..<MonthMath.leadingBlanks(anchor), id: \.self) { _ in
                            Color.clear.frame(height: 40)
                        }
                        ForEach(MonthMath.days(in: anchor), id: \.wireString) { date in
                            dayCell(date)
                        }
                    }
                }
                .padding()
            }
            HStack {
                Button("Cancel") { dismiss() }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
                Spacer()
            }
            .padding()
        }
        .themeBackground()
        #if os(macOS)
        .frame(minWidth: 380, idealWidth: 400, minHeight: 420, idealHeight: 460)
        #endif
        #if os(iOS)
        .presentationDetents([.medium])
        #endif
    }

    private var monthHeader: some View {
        HStack {
            Button { anchor = MonthMath.addingMonths(anchor, -1) } label: {
                Image(systemName: "chevron.left").font(.body.weight(.semibold))
            }
            .buttonStyle(.plain)

            Spacer()
            Text(MonthMath.monthTitle(anchor)).font(.headline)
            Spacer()

            Button { anchor = MonthMath.addingMonths(anchor, 1) } label: {
                Image(systemName: "chevron.right").font(.body.weight(.semibold))
                    .foregroundStyle(canGoForward ? Color.accentColor : DB.fillStrong(scheme))
            }
            .buttonStyle(.plain)
            .disabled(!canGoForward)
        }
        .glassCard(padding: 14)
    }

    private var canGoForward: Bool {
        MonthMath.firstOfMonth(DayDate.todayLocal()).wireString != anchor.wireString
            && !MonthMath.isFutureMonth(anchor)
    }

    private func dayCell(_ date: DayDate) -> some View {
        let isFuture = MonthMath.isAfterToday(date)
        let isToday = date.wireString == DayDate.todayLocal().wireString
        let isSelected = date.wireString == selected.wireString
        return Button {
            Haptics.tick()
            onPick(date)
            dismiss()
        } label: {
            Text("\(date.day)")
                .font(.subheadline.weight(isToday ? .bold : .regular))
                .foregroundStyle(isFuture ? Color.secondary.opacity(0.5) : Color.primary)
                .frame(maxWidth: .infinity)
                .frame(height: 40)
                .background(
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .fill(isSelected ? Color.accentColor.opacity(0.16) : DB.fillSubtle(scheme)))
                .overlay(
                    RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(isToday ? Color.accentColor : Color.clear, lineWidth: 1.5))
        }
        .buttonStyle(.plain)
        .disabled(isFuture)
    }
}

/// Decline a logged drive with an optional reason.
private struct DrivingRejectSheet: View {
    let entry: DrivingLogEntry
    var onDecline: (String?) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var reason = ""

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Decline drive")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard(title: "Reason (optional)") {
                        TextField("A short note they'll see", text: $reason, axis: .vertical)
                            .lineLimit(2...4).sheetFieldBackground()
                    }
                }
                .padding(.horizontal).padding(.top, 4)
            }
            SheetActionBar(saveTitle: "Decline", saving: false, canSave: true,
                           onCancel: { dismiss() },
                           onSave: { onDecline(reason); dismiss() })
                .padding()
        }
        .themeBackground()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 300, idealHeight: 320)
        #endif
        #if os(iOS)
        .presentationDetents([.medium])
        #endif
    }
}
