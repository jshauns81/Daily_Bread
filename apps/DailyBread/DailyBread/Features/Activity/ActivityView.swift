import SwiftUI
import DailyBreadKit

/// The parent's day command center: any date's chore table with the full
/// action set — approve (the gold moment), reject, excuse, undo/restore,
/// and Help response. The native twin of the web Activity screen.
/// `userId` is nil on the tab (resolves to the only child); the Home
/// drill-in passes the child explicitly.
@MainActor
@Observable
final class ActivityStore {
    let targetUserId: String?

    var date = DayDate.todayLocal()
    var day: TodayChores?
    var weekStart: DayDate?
    /// choreDefinitionId → live per-instance minute price this week, for the
    /// reject consequence line (minutes only — Importance never surfaces).
    var chorePrices: [Int: Int] = [:]
    var loading = false
    var errorMessage: String?
    var justApprovedId: Int?
    var helpTarget: HelpRequest?
    /// Multi-child only: the chooser's pick. Single-child never sets this.
    var selectedChildId: String?

    init(targetUserId: String? = nil) {
        self.targetUserId = targetUserId
    }

    func childId(_ session: SessionStore) -> String? {
        targetUserId
            ?? session.onlyChild?.userId
            ?? selectedChildId
            ?? session.children.first?.userId
    }

    func load(_ session: SessionStore) async {
        let userId = childId(session)
        loading = day == nil
        defer { loading = false }
        do {
            day = try await session.client.todayChores(date: date, userId: userId)
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
        // Week boundary + chore minute prices ride along; losing them only
        // drops the settled-week notice and the reject consequence line.
        if let summary = try? await session.client.screenTime(userId: userId) {
            weekStart = summary.weekStart
            chorePrices = Dictionary(
                uniqueKeysWithValues: summary.chorePrices.map { ($0.choreDefinitionId, $0.perInstanceMinutes) })
        }
    }

    func step(_ delta: Int) {
        Haptics.tick()
        date = date.addingDays(delta)
    }

    func goToToday() {
        Haptics.tick()
        date = .todayLocal()
    }

    // MARK: - Actions

    /// The shared optimistic shape: flip the row, call, adopt server truth
    /// on success, roll back on failure.
    private func setStatus(
        _ item: ChoreItem,
        to optimistic: String,
        _ session: SessionStore,
        call: () async throws -> Void
    ) async {
        guard let index = day?.items.firstIndex(where: { $0.id == item.id }) else { return }
        let original = day?.items[index].status ?? item.status
        withAnimation(.snappy) { day?.items[index].status = optimistic }
        Haptics.tick()
        do {
            try await call()
            await load(session)
        } catch {
            withAnimation(.snappy) { day?.items[index].status = original }
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }

    func excuse(_ item: ChoreItem, _ session: SessionStore) async {
        await setStatus(item, to: "Skipped", session) {
            try await session.client.excuseChore(choreDefinitionId: item.choreDefinitionId, date: date)
        }
    }

    func reject(_ item: ChoreItem, _ session: SessionStore) async {
        await setStatus(item, to: "Missed", session) {
            try await session.client.missChore(choreDefinitionId: item.choreDefinitionId, date: date)
        }
    }

    /// Undo (of an approval) and Restore (of Missed/Skipped) — same call.
    func reset(_ item: ChoreItem, _ session: SessionStore) async {
        await setStatus(item, to: "Pending", session) {
            try await session.client.resetChore(choreDefinitionId: item.choreDefinitionId, date: date)
        }
    }

    /// Held while a parent action is in flight (see TodayStore.isMutating).
    var isMutating = false

    func approve(_ item: ChoreItem, _ session: SessionStore) async {
        guard let logId = item.choreLogId else { return }
        do {
            try await session.client.approve(choreLogId: logId)
            justApprovedId = item.choreDefinitionId
            Haptics.success()
            try? await Task.sleep(for: .seconds(0.9))
            withAnimation(.snappy) { justApprovedId = nil }
            await load(session)
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }

    func respondHelp(_ request: HelpRequest, _ kind: HelpResponseKind, _ session: SessionStore) async {
        do {
            try await session.client.respondToHelp(choreLogId: request.choreLogId, response: kind)
            Haptics.success()
            await load(session)
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct ActivityView: View {
    var title: String = "Activity"

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store: ActivityStore

    /// Row-morph confirmations (system dialogs are banned): the row itself
    /// becomes the question. Keyed by choreDefinitionId.
    @State private var confirmingExcuseId: Int?
    @State private var confirmingRejectId: Int?

    init(userId: String? = nil, title: String = "Activity") {
        self.title = title
        _store = State(initialValue: ActivityStore(targetUserId: userId))
    }

    private var isToday: Bool { store.date == .todayLocal() }

    private var taskKey: String {
        "\(store.date.wireString)|\(store.selectedChildId ?? store.targetUserId ?? "")"
    }

    var body: some View {
        List {
            Section {
                dayNavigator
                    .listRowBackground(Color.clear)
                    .listRowInsets(EdgeInsets())
            }

            if let weekStart = store.weekStart, store.date < weekStart {
                Section {
                    Label("A settled week — this updates the record, but screen time and payouts already granted won't change.",
                          systemImage: "clock.arrow.circlepath")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            if let day = store.day {
                if !day.items.isEmpty {
                    Section {
                        statTiles(day)
                            .listRowBackground(Color.clear)
                            .listRowInsets(EdgeInsets())
                    }
                    Section {
                        // Server owns the order — never re-sort client-side.
                        ForEach(day.items) { item in
                            choreRow(item)
                        }
                    }
                } else {
                    ContentUnavailableView(
                        "No chores this day",
                        systemImage: "calendar.badge.minus",
                        description: Text(store.date.longDisplay))
                }
            } else if store.loading {
                ProgressView().frame(maxWidth: .infinity)
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle(title)
        .themeBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .poll(isPaused: { store.isMutating }) { await store.load(session) }
        .task(id: taskKey) {
            withAnimation(.snappy) {
                confirmingExcuseId = nil
                confirmingRejectId = nil
            }
            await store.load(session)
        }
        .sheet(item: Binding(
            get: { store.helpTarget },
            set: { store.helpTarget = $0 })
        ) { request in
            HelpRespondSheet(request: request) { kind in
                Task { await store.respondHelp(request, kind, session) }
            }
        }
    }

    // MARK: - Day navigation

    private var dayNavigator: some View {
        VStack(spacing: 10) {
            if store.targetUserId == nil && session.children.count > 1 {
                childChooser
            }
            HStack {
                Button {
                    store.step(-1)
                } label: {
                    Image(systemName: "chevron.left")
                        .font(.body.weight(.semibold))
                }
                .buttonStyle(.plain)

                Spacer()
                VStack(spacing: 2) {
                    Text(store.date.longDisplay)
                        .font(.headline)
                    if isToday {
                        Text("Today")
                            .font(.caption2.weight(.bold))
                            .foregroundStyle(Color.dbAccent)
                    } else {
                        Button {
                            store.goToToday()
                        } label: {
                            Text("Back to today")
                                .font(.caption2.weight(.semibold))
                                .foregroundStyle(Color.dbAccent)
                        }
                        .contentShape(Rectangle())
                        .buttonStyle(.plain)
                    }
                }
                Spacer()

                Button {
                    store.step(1)
                } label: {
                    Image(systemName: "chevron.right")
                        .font(.body.weight(.semibold))
                }
                .contentShape(Rectangle())
                .buttonStyle(.plain)
            }
            .glassCard(padding: 14)
        }
    }

    /// Multi-child only — single-child mode never shows a chooser.
    private var childChooser: some View {
        Menu {
            ForEach(session.children) { child in
                Button(child.name) {
                    store.selectedChildId = child.userId
                }
            }
        } label: {
            HStack(spacing: 6) {
                Text(chooserLabel)
                    .font(.subheadline.weight(.semibold))
                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption2.weight(.bold))
            }
            .foregroundStyle(Color.dbAccent)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var chooserLabel: String {
        let id = store.childId(session)
        return session.children.first { $0.userId == id }?.name ?? "Child"
    }

    // MARK: - Stat tiles

    private func statTiles(_ day: TodayChores) -> some View {
        let done = day.items.filter(\.isDone)
        let earned = Money(done.reduce(Decimal.zero) { $0 + $1.earnValue.amount })
        return HStack(spacing: 12) {
            statTile(value: "\(day.items.count)", label: "chores", color: .primary)
            statTile(value: "\(done.count)", label: "done", color: DB.success(scheme))
            statTile(value: "\(day.items.filter(\.isPending).count)", label: "pending", color: .accentColor)
            statTile(value: earned.display, label: "earned", color: DB.gold(scheme))
        }
    }

    private func statTile(value: String, label: String, color: Color) -> some View {
        VStack(spacing: 3) {
            Text(value)
                .font(.title3.weight(.bold))
                .foregroundStyle(color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .glassCard(padding: 12)
    }

    // MARK: - Rows

    @ViewBuilder
    private func choreRow(_ item: ChoreItem) -> some View {
        if confirmingExcuseId == item.id {
            excuseConfirmRow(item)
        } else if confirmingRejectId == item.id {
            rejectConfirmRow(item)
        } else {
            normalRow(item)
        }
    }

    private func normalRow(_ item: ChoreItem) -> some View {
        let isGlowing = store.justApprovedId == item.id
        return HStack(spacing: 12) {
            Text((item.icon ?? "•").firstGlyph)
                .font(.title3)
            VStack(alignment: .leading, spacing: 2) {
                Text(item.name)
                    .font(.body.weight(.medium))
                if let caption = statusCaption(item) {
                    Text(caption.text)
                        .font(.caption)
                        .foregroundStyle(caption.color)
                        .lineLimit(2)
                }
            }
            Spacer()
            if item.isEarning {
                Text(item.earnValue.display)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(DB.gold(scheme))
                    .opacity(item.isDone ? 0.6 : 1)
            }
            actions(item, isGlowing: isGlowing)
        }
        .padding(.vertical, 2)
        .listRowBackground(isGlowing ? DB.glow(scheme).opacity(0.18) : nil)
        .animation(.easeOut(duration: 0.4), value: isGlowing)
    }

    private func statusCaption(_ item: ChoreItem) -> (text: String, color: Color)? {
        if item.isHelp {
            return ("“\(item.helpReason ?? "Help raised")”", DB.help(scheme))
        }
        if item.isApproved {
            let by = item.approvedByUserName.map { " by \($0)" } ?? ""
            return ("Approved\(by)", DB.gold(scheme))
        }
        if item.isSkipped { return ("Excused for the day", .secondary) }
        if item.isMissed { return ("Missed", DB.help(scheme)) }
        if item.isDone { return ("Done — awaiting review", .secondary) }
        if item.weeklyTargetCount > 0 {
            return ("\(item.weeklyCompletedCount)/\(item.weeklyTargetCount) this week", .secondary)
        }
        return nil
    }

    @ViewBuilder
    private func actions(_ item: ChoreItem, isGlowing: Bool) -> some View {
        if item.isDone && !item.isApproved {
            // Completed, awaiting review: the gold moment + a quiet reject.
            Button {
                Task { await store.approve(item, session) }
            } label: {
                Label(isGlowing ? "Approved" : "Approve",
                      systemImage: isGlowing ? "checkmark" : "hand.thumbsup")
                    .font(.subheadline.weight(.bold))
            }
            .buttonStyle(.borderedProminent)
            .tint(DB.gold(scheme))
            .disabled(isGlowing)

            Button {
                withAnimation(.snappy) { confirmingRejectId = item.id }
            } label: {
                Image(systemName: "xmark")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.bordered)
        } else if item.isPending {
            quietAction("Excuse") {
                withAnimation(.snappy) { confirmingExcuseId = item.id }
            }
        } else if item.isApproved {
            quietAction("Undo") {
                Task { await store.reset(item, session) }
            }
        } else if item.isMissed || item.isSkipped {
            quietAction("Restore") {
                Task { await store.reset(item, session) }
            }
        } else if item.isHelp {
            Button("Respond") {
                store.helpTarget = helpRequest(item)
            }
            .buttonStyle(.bordered)
            .tint(DB.help(scheme))
            .font(.subheadline.weight(.semibold))
        }
    }

    private func quietAction(_ label: String, action: @escaping () -> Void) -> some View {
        Button(label, action: action)
            .buttonStyle(.bordered)
            .tint(.secondary)
            .font(.caption.weight(.semibold))
    }

    /// Excuse is forgiving, so the confirm wears the accent, not red.
    private func excuseConfirmRow(_ item: ChoreItem) -> some View {
        HStack(spacing: 10) {
            Text("Excuse ‘\(item.name)’ for this day? No earning, no screen-time hit — undoable.")
                .font(.subheadline)
            Spacer()
            Button("Keep") {
                withAnimation(.snappy) { confirmingExcuseId = nil }
            }
            .buttonStyle(.bordered)
            .tint(.secondary)
            .font(.caption.weight(.semibold))
            Button("Excuse") {
                withAnimation(.snappy) { confirmingExcuseId = nil }
                Task { await store.excuse(item, session) }
            }
            .buttonStyle(.borderedProminent)
            .font(.caption.weight(.bold))
        }
        .listRowBackground(Color.dbAccent.opacity(0.08))
    }

    private func rejectConfirmRow(_ item: ChoreItem) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Mark ‘\(item.name)’ as not done?")
                    .font(.subheadline)
                if let minutes = store.chorePrices[item.choreDefinitionId] {
                    Text("Up to \(ScreenTimeFormat.minutes(minutes)) of screen time may be lost (more if missed repeatedly).")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button("Keep") {
                withAnimation(.snappy) { confirmingRejectId = nil }
            }
            .buttonStyle(.bordered)
            .tint(.secondary)
            .font(.caption.weight(.semibold))
            Button("Mark missed") {
                withAnimation(.snappy) { confirmingRejectId = nil }
                Task { await store.reject(item, session) }
            }
            .buttonStyle(.borderedProminent)
            .tint(DB.help(scheme))
            .font(.caption.weight(.bold))
        }
        .listRowBackground(DB.help(scheme).opacity(0.08))
    }

    // MARK: - Help

    /// The Help sheet wants a HelpRequest; synthesize one from the row.
    private func helpRequest(_ item: ChoreItem) -> HelpRequest? {
        guard let logId = item.choreLogId else { return nil }
        let childName = store.day?.userName
            ?? session.onlyChild?.name
            ?? "Your child"
        return HelpRequest(
            choreLogId: logId,
            choreDefinitionId: item.choreDefinitionId,
            choreName: item.name,
            childName: childName,
            childUserId: store.childId(session),
            earnValue: item.earnValue,
            reason: item.helpReason,
            date: store.date,
            requestedAtUtc: item.helpRequestedAtUtc)
    }
}
