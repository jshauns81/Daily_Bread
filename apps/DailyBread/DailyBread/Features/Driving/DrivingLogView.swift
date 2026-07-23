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
        .graphiteBackground()
        .toolbar {
            if mode == .kid {
                ToolbarItem(placement: .primaryAction) {
                    Button { logging = true } label: { Image(systemName: "plus") }
                }
            }
        }
        .sheet(isPresented: $logging) {
            DriveEditorSheet { await store.load(session, mode: mode) }
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
                    .foregroundStyle(entry.isNightDriving ? Color.indigo : Color.accentColor)
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
                .background(Color.secondary.opacity(0.15), in: Capsule())
        }
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label(mode == .parent ? "Nothing waiting" : "No drives logged",
                  systemImage: mode == .parent ? "checkmark.circle" : "car")
        } description: {
            Text(mode == .parent
                 ? "Logged drives waiting for your approval will show up here."
                 : "Tap + to log a supervised drive. It counts toward your hours once a parent approves.")
        }
        .frame(maxWidth: .infinity).padding(.vertical, 20)
    }
}

/// Log a supervised drive.
private struct DriveEditorSheet: View {
    var onSaved: () async -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var date = Date()
    @State private var start = Date()
    @State private var end = Date()
    @State private var supervisor = ""
    @State private var weather: DrivingWeather = .clear
    @State private var nightMode = 0   // 0 auto, 1 day, 2 night
    @State private var notes = ""
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Log a drive")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard(title: "When") {
                        DatePicker("Date", selection: $date, in: ...Date(), displayedComponents: .date)
                        DatePicker("Start", selection: $start, displayedComponents: .hourAndMinute)
                        DatePicker("End", selection: $end, displayedComponents: .hourAndMinute)
                    }
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
                           canSave: !supervisor.trimmingCharacters(in: .whitespaces).isEmpty,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 440, idealWidth: 480, minHeight: 480, idealHeight: 520)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    private func hhmm(_ d: Date) -> String {
        let c = Calendar.current.dateComponents([.hour, .minute], from: d)
        return String(format: "%02d:%02d", c.hour ?? 0, c.minute ?? 0)
    }

    private func dayDate(_ d: Date) -> DayDate {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: d)
        return DayDate(year: c.year ?? 2000, month: c.month ?? 1, day: c.day ?? 1)
    }

    private func save() async {
        if hhmm(start) == hhmm(end) { errorMessage = "Start and end can't be the same."; return }
        saving = true
        defer { saving = false }
        errorMessage = nil
        let create = DrivingLogCreate(
            date: dayDate(date),
            startTime: hhmm(start),
            endTime: hhmm(end),
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
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 300, idealHeight: 320)
        #endif
        #if os(iOS)
        .presentationDetents([.medium])
        #endif
    }
}
