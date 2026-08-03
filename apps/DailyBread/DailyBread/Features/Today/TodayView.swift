import SwiftUI
import DailyBreadKit

/// The daily driver: today's chores, tap to complete (optimistic), raise
/// Help when stuck. Completing pays off visibly: the earn value pops, the
/// ring climbs, and finishing the whole day rains confetti.
/// Parents reuse this same screen for a kid via `userId` (help is hidden —
/// raising Help is the kid's own act).
@MainActor
@Observable
final class TodayStore {
    let targetUserId: String?

    var today: TodayChores?
    var balance: Money?
    var streak = 0
    var loading = false
    var errorMessage: String?
    var helpTarget: ChoreItem?

    /// Set when a completion earns money — drives the "+$2.50" pop.
    var earnPop: (amount: Money, at: Date)?
    /// Set when the day's last chore completes — drives the tier-3 celebration.
    var celebrationStart: Date?
    /// Set when the last EARNING chore lands — drives the tier-2 coin arc.
    var coinFlight: (choreId: Int, start: Date)?
    /// Incremented when the coin arrives; the header money line pulses on change.
    var balancePulse = 0
    /// True while a toggle is in flight. A poll landing mid-tap would replace
    /// the optimistic row with the server's not-yet-updated one and the check
    /// would visibly bounce back.
    var isMutating = false

    init(targetUserId: String? = nil) {
        self.targetUserId = targetUserId
    }

    func load(_ session: SessionStore) async {
        loading = today == nil
        defer { loading = false }
        do {
            async let todayTask = session.client.todayChores(userId: targetUserId)
            async let balanceTask = session.client.balance(userId: targetUserId)
            // Server order = the planner's order. Stable every day, and rows
            // never jump around as they're checked off.
            today = try await todayTask
            balance = try await balanceTask.balance
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
        await loadStreak(session)
        publishWidgetSnapshot()
    }

    /// The §1.2a widget family renders from what this store just learned.
    private func publishWidgetSnapshot() {
        guard today != nil else { return }
        WidgetBridge.mergeToday(
            done: doneCount, total: totalCount,
            earned: earnedToday.display,
            balance: balance?.display,
            streak: streak,
            childName: today?.userName)
    }

    /// Streak = consecutive all-complete days ending today (or yesterday if
    /// today isn't finished yet). Computed from the calendar range; days
    /// with no chores scheduled don't break it.
    private func loadStreak(_ session: SessionStore) async {
        let today = DayDate.todayLocal()
        let windowStart = DayDate(year: today.month > 2 ? today.year : today.year - 1,
                                  month: max(1, today.month - 2), day: 1)
        guard let range = try? await session.client.calendarRange(
            from: windowStart, to: today, userId: targetUserId) else { return }

        var count = 0
        for day in range.days.reversed() {
            switch day.status {
            case "AllComplete":
                count += 1
            case "NoChores", "Future":
                continue
            default:
                // Today being unfinished doesn't break the streak.
                if day.date == today { continue }
                streak = count
                return
            }
        }
        streak = count
    }

    func toggle(_ item: ChoreItem, _ session: SessionStore) async {
        guard var snapshot = today,
              let index = snapshot.items.firstIndex(where: { $0.id == item.id }) else { return }

        isMutating = true
        defer { isMutating = false }

        let original = snapshot.items[index].status
        let completing = !item.isDone
        // Predict the server: auto-approve chores land as Approved (the done
        // state), the rest as Completed (awaiting approval). Guessing wrong
        // would flash the dashed waiting ring before every done bloom.
        snapshot.items[index].status = completing
            ? ((item.autoApprove ?? true) ? "Approved" : "Completed")
            : "Pending"
        withAnimation(.snappy) { today = snapshot }
        // Haptic on check only — undo is silent. Don't reward undo.
        if completing { Haptics.rigid() }

        do {
            let result = try await session.client.toggleChore(
                choreDefinitionId: item.choreDefinitionId,
                date: snapshot.date,
                userId: targetUserId)
            if var current = today,
               let i = current.items.firstIndex(where: { $0.id == item.id }) {
                current.items[i].status = result.status
                withAnimation(.snappy) { today = current }
            }
            publishWidgetSnapshot()
            if completing {
                celebrate(item)
            }
        } catch {
            if var current = today,
               let i = current.items.firstIndex(where: { $0.id == item.id }) {
                current.items[i].status = original
                withAnimation(.snappy) { today = current }
            }
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }

    private func celebrate(_ item: ChoreItem) {
        if !item.earnValue.isZero {
            withAnimation(.snappy) { earnPop = (item.earnValue, Date()) }
            Task {
                try? await Task.sleep(for: .seconds(1.4))
                withAnimation(.easeOut) { earnPop = nil }
            }
        }
        if doneCount == totalCount, totalCount > 0 {
            // Tier 3: a genuinely perfect day.
            celebrationStart = Date()
            Haptics.success()
            Task {
                try? await Task.sleep(for: .seconds(2.8))
                celebrationStart = nil
            }
        } else if item.isEarning, allEarningDone {
            // Tier 2: the last earning chore landed — coin arcs to the balance.
            coinFlight = (item.id, Date())
            Task {
                try? await Task.sleep(for: .seconds(0.62))
                balancePulse += 1
                try? await Task.sleep(for: .seconds(0.6))
                coinFlight = nil
            }
        }
    }

    private var allEarningDone: Bool {
        let earning = (today?.items ?? []).filter(\.isEarning)
        return !earning.isEmpty && earning.allSatisfy(\.isDone)
    }

    func raiseHelp(_ item: ChoreItem, reason: String, _ session: SessionStore) async {
        do {
            try await session.client.raiseHelp(
                choreDefinitionId: item.choreDefinitionId,
                date: today?.date,
                reason: reason)
            await load(session)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    var doneCount: Int { today?.items.filter(\.isDone).count ?? 0 }
    var totalCount: Int { today?.items.count ?? 0 }
    var allDone: Bool { totalCount > 0 && doneCount == totalCount }

    var earnedToday: Money {
        let sum = (today?.items ?? [])
            .filter(\.isDone)
            .reduce(Decimal.zero) { $0 + $1.earnValue.amount }
        return Money(sum)
    }
}

struct TodayView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var store: TodayStore
    @State private var moneyPulsing = false

    private let title: String
    /// Viewing someone else (parent drill-in) hides Help — it's the kid's act.
    private var isSelf: Bool { store.targetUserId == nil }

    init(userId: String? = nil, title: String = "Today") {
        _store = State(initialValue: TodayStore(targetUserId: userId))
        self.title = title
    }

    var body: some View {
        List {
            if let today = store.today {
                Section {
                    header(today)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
                }

                // Stakes visible first: what's on the line before the list.
                Section {
                    AtRiskCard(userId: store.targetUserId)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
                }

                Section {
                    ForEach(today.items) { item in
                        ChoreRow(item: item, allowHelp: isSelf, isParentActing: !isSelf) {
                            Task { await store.toggle(item, session) }
                        } onHelp: {
                            store.helpTarget = item
                        }
                    }
                }

                if store.allDone {
                    Section {
                        Label("Day complete — every chore done ✨",
                              systemImage: "party.popper")
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(DB.gold(scheme))
                            .frame(maxWidth: .infinity)
                            .listRowBackground(DB.gold(scheme).opacity(0.1))
                    }
                }

                Section {
                    ScreenTimeCard(userId: store.targetUserId)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
                }

                Section {
                    RainbowYearCard(title: isSelf ? "Your year" : "The year",
                                    userId: store.targetUserId,
                                    updatesDockIcon: isSelf && session.currentUser?.isParent != true)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
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
        .poll(isPaused: { store.isMutating }) { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
        .overlayPreferenceValue(CelebrationAnchorsKey.self) { anchors in
            GeometryReader { proxy in
                if let flight = store.coinFlight,
                   session.features.enableConfetti, !reduceMotion,
                   let source = anchors.checks[flight.choreId],
                   let target = anchors.balance {
                    CoinArcLayer(start: flight.start,
                                 from: proxy[source], to: proxy[target])
                }
            }
            .allowsHitTesting(false)
        }
        .overlay {
            if let start = store.celebrationStart, session.features.enableConfetti {
                PerfectDayCelebration(start: start)
            }
        }
        .onChange(of: store.balancePulse) {
            moneyPulsing = true
            Task {
                try? await Task.sleep(for: .milliseconds(230))
                moneyPulsing = false
            }
        }
        .sheet(item: $store.helpTarget) { item in
            HelpSheet(item: item) { reason in
                Task { await store.raiseHelp(item, reason: reason, session) }
            }
        }
    }

    private func header(_ today: TodayChores) -> some View {
        HStack(spacing: 16) {
            ZStack(alignment: .top) {
                ProgressRing(
                    progress: store.totalCount == 0 ? 0 : Double(store.doneCount) / Double(store.totalCount),
                    label: "\(store.doneCount)/\(store.totalCount)")
                    .frame(width: 64, height: 64)

                if let pop = store.earnPop {
                    Text("+\(pop.amount.display)")
                        .font(.headline.weight(.heavy))
                        .foregroundStyle(DB.gold(scheme))
                        .offset(y: -26)
                        .transition(.asymmetric(
                            insertion: .offset(y: 14).combined(with: .opacity),
                            removal: .offset(y: -10).combined(with: .opacity)))
                }
            }

            VStack(alignment: .leading, spacing: 2) {
                if isSelf {
                    (Text("\(Greeting.current), ")
                        + Text((today.userName ?? "there").capitalized)
                            .foregroundStyle(Color.dbAccent)
                        + Text("."))
                        .font(.headline)
                }
                Text(today.date.longDisplay)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                HStack(spacing: 6) {
                    Text(store.earnedToday.display)
                        .foregroundStyle(DB.gold(scheme))
                        .fontWeight(.bold)
                    Text("today")
                        .foregroundStyle(.secondary)
                    if let balance = store.balance {
                        Text("·").foregroundStyle(.tertiary)
                        Text(balance.display)
                            .foregroundStyle(DB.gold(scheme))
                            .fontWeight(.semibold)
                        Text("saved")
                            .foregroundStyle(.secondary)
                    }
                }
                .font(.subheadline)
                .scaleEffect(moneyPulsing ? 1.28 : 1, anchor: .leading)
                .animation(.spring(response: 0.23, dampingFraction: 0.5), value: moneyPulsing)
                .anchorPreference(key: CelebrationAnchorsKey.self, value: .center) {
                    CelebrationAnchors(balance: $0)
                }

                if store.streak > 1 && session.features.enableStreaks {
                    Text("🔥 \(store.streak)-day streak")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(DB.gold(scheme))
                        .padding(.top, 1)
                }
            }
            Spacer()
        }
        .glassCard()
    }
}

struct ChoreRow: View {
    let item: ChoreItem
    var allowHelp: Bool = true
    /// A parent drilled into the kid's day: tapping an awaiting-approval check
    /// approves it. For the kid it's terminal — the check wiggles instead.
    var isParentActing: Bool = false
    var onToggle: () -> Void
    var onHelp: () -> Void

    @Environment(\.colorScheme) private var scheme

    var body: some View {
        HStack(spacing: 12) {
            Text((item.icon ?? "🧺").firstGlyph)
                .font(.title3)
                .frame(width: 40, height: 40)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 10, style: .continuous))

            VStack(alignment: .leading, spacing: 2) {
                Text(item.name)
                    .font(.body.weight(.medium))
                    .strikethrough(item.isDone, color: .secondary)
                    .foregroundStyle(item.isDone ? .secondary : .primary)
                if item.isHelp {
                    Text("Help raised — protected")
                        .font(.caption)
                        .foregroundStyle(DB.help(scheme))
                } else if item.isApproved, let by = item.approvedByUserName {
                    Text("Approved by \(by)")
                        .font(.caption)
                        .foregroundStyle(DB.gold(scheme))
                } else if item.checkState == .awaitingApproval {
                    Text("Done — waiting for approval")
                        .font(.caption)
                        .foregroundStyle(DB.gold(scheme))
                } else if item.scheduleType == "WeeklyFrequency", item.weeklyTargetCount > 0 {
                    Text("\(item.weeklyCompletedCount) of \(item.weeklyTargetCount) this week")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            Spacer()

            if item.isEarning {
                Text(item.earnValue.display)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(item.isDone ? DB.gold(scheme).opacity(0.5) : DB.gold(scheme))
            }

            ChoreCheck(
                state: item.checkState,
                isEarning: item.isEarning,
                onToggle: onToggle,
                onTerminalTap: isParentActing && item.checkState == .awaitingApproval
                    ? onToggle : nil)
                .anchorPreference(key: CelebrationAnchorsKey.self, value: .center) {
                    CelebrationAnchors(checks: [item.id: $0])
                }

            // Help stays a visible affordance — a safety valve the kid can't see
            // isn't one. Raising Help is never hidden behind the check control.
            if allowHelp && !item.isDone && !item.isHelp {
                Button(action: onHelp) {
                    DBIcon("questionmark.circle", tint: DB.help(scheme))
                        .frame(width: 34, height: 44)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Raise Help")
            }
        }
        .contentShape(Rectangle())
        .swipeActions(edge: .leading, allowsFullSwipe: true) {
            if !item.isHelp {
                Button(item.isDone ? "Undo" : "Done") { onToggle() }
                    .tint(item.isDone ? Color.secondary : Color.dbAccent)
            }
        }
        .swipeActions(edge: .trailing) {
            if allowHelp && !item.isDone && !item.isHelp {
                Button("Help") { onHelp() }
                    .tint(DB.help(scheme))
            }
        }
    }
}

struct HelpSheet: View {
    let item: ChoreItem
    var onSubmit: (String) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @State private var reason = ""

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Raising Help on **\(item.name)** protects it from tonight's penalty until \(session.voice.parent) responds.")
                        .font(.subheadline)
                }
                Section("What's going on?") {
                    TextField("I need a hand because…", text: $reason, axis: .vertical)
                        .lineLimit(3...6)
                }
            }
            .navigationTitle("Raise Help")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Raise Help") {
                        onSubmit(reason)
                        dismiss()
                    }
                    .disabled(reason.trimmingCharacters(in: .whitespaces).isEmpty)
                    .tint(DB.help(scheme))
                }
            }
        }
        #if os(iOS)
        .presentationDetents([.medium])
        #endif
    }
}

struct ProgressRing: View {
    var progress: Double
    var label: String

    var body: some View {
        ZStack {
            Circle()
                .stroke(.quaternary, lineWidth: 7)
            Circle()
                .trim(from: 0, to: min(1, progress))
                .stroke(Color.dbAccent, style: StrokeStyle(lineWidth: 7, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.snappy, value: progress)
            Text(label)
                .font(.subheadline.weight(.bold))
        }
    }
}
