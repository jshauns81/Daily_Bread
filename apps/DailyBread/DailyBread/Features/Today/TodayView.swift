import SwiftUI
import DailyBreadKit

/// The kid's daily driver: today's chores, tap to complete (optimistic),
/// raise Help when stuck. The server owns "today".
@MainActor
@Observable
final class TodayStore {
    var today: TodayChores?
    var loading = false
    var errorMessage: String?
    var helpTarget: ChoreItem?

    func load(_ session: SessionStore) async {
        loading = today == nil
        defer { loading = false }
        do {
            today = try await session.client.todayChores()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Optimistic toggle: flip locally, reconcile with the server's answer,
    /// roll back on failure. Never queue offline writes (plan §Phase 2).
    func toggle(_ item: ChoreItem, _ session: SessionStore) async {
        guard var snapshot = today,
              let index = snapshot.items.firstIndex(where: { $0.id == item.id }) else { return }

        let original = snapshot.items[index].status
        snapshot.items[index].status = item.isDone ? "Pending" : "Completed"
        today = snapshot
        Haptics.tick()

        do {
            let result = try await session.client.toggleChore(
                choreDefinitionId: item.choreDefinitionId,
                date: snapshot.date)
            if var current = today,
               let i = current.items.firstIndex(where: { $0.id == item.id }) {
                current.items[i].status = result.status
                today = current
            }
        } catch {
            if var current = today,
               let i = current.items.firstIndex(where: { $0.id == item.id }) {
                current.items[i].status = original
                today = current
            }
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
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
    @State private var store = TodayStore()

    var body: some View {
        List {
            if let today = store.today {
                Section {
                    header(today)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                }

                Section {
                    ForEach(today.items) { item in
                        ChoreRow(item: item) {
                            Task { await store.toggle(item, session) }
                        } onHelp: {
                            store.helpTarget = item
                        }
                    }
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
        .navigationTitle("Today")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
        .sheet(item: $store.helpTarget) { item in
            HelpSheet(item: item) { reason in
                Task { await store.raiseHelp(item, reason: reason, session) }
            }
        }
    }

    private func header(_ today: TodayChores) -> some View {
        HStack(spacing: 16) {
            ProgressRing(
                progress: store.totalCount == 0 ? 0 : Double(store.doneCount) / Double(store.totalCount),
                label: "\(store.doneCount)/\(store.totalCount)")
                .frame(width: 64, height: 64)

            VStack(alignment: .leading, spacing: 2) {
                Text(today.date.longDisplay)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                HStack(spacing: 4) {
                    Text(store.earnedToday.display)
                        .font(.headline)
                        .foregroundStyle(DB.gold(scheme))
                    Text("earned today")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                if store.doneCount < store.totalCount {
                    Text("\(store.totalCount - store.doneCount) to go")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
        .glassCard()
    }
}

struct ChoreRow: View {
    let item: ChoreItem
    var onToggle: () -> Void
    var onHelp: () -> Void

    @Environment(\.colorScheme) private var scheme

    var body: some View {
        HStack(spacing: 12) {
            Text(item.icon ?? "🧺")
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
                }
            }

            Spacer()

            if item.isEarning {
                Text(item.earnValue.display)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(item.isDone ? DB.gold(scheme).opacity(0.5) : DB.gold(scheme))
            }

            if item.isHelp {
                Text("HELP")
                    .font(.caption2.weight(.heavy))
                    .padding(.horizontal, 8)
                    .padding(.vertical, 4)
                    .background(DB.help(scheme), in: Capsule())
                    .foregroundStyle(.white)
            } else {
                Button(action: onToggle) {
                    Image(systemName: item.isDone ? "checkmark.circle.fill" : "circle")
                        .font(.title2)
                        .foregroundStyle(item.isDone ? Color.accentColor : Color.secondary)
                }
                .buttonStyle(.plain)
            }
        }
        .contentShape(Rectangle())
        .swipeActions(edge: .trailing) {
            if !item.isDone && !item.isHelp {
                Button("Help") { onHelp() }
                    .tint(DB.help(scheme))
            }
        }
    }
}

struct HelpSheet: View {
    let item: ChoreItem
    var onSubmit: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @State private var reason = ""

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("Raising Help on **\(item.name)** protects it from tonight's penalty until a parent responds.")
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
                .stroke(Color.accentColor, style: StrokeStyle(lineWidth: 7, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.snappy, value: progress)
            Text(label)
                .font(.subheadline.weight(.bold))
        }
    }
}
