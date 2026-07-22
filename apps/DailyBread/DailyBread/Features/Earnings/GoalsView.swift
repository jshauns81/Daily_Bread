import SwiftUI
import DailyBreadKit

/// Savings goals, owned by the person saving. Lists every goal with its
/// progress, lets you crown one as the "primary" (the one that shows on Home
/// and Earnings), and add / edit / remove them. Operates on the signed-in
/// user's own goals — the same set Earnings reads — so there's no "whose goals"
/// question to answer.
@MainActor
@Observable
final class GoalsStore {
    var goals: [Goal] = []
    var loading = false
    var errorMessage: String?
    var busy = false

    func load(_ session: SessionStore) async {
        loading = goals.isEmpty
        defer { loading = false }
        do {
            goals = try await session.client.goals()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func setPrimary(_ goal: Goal, _ session: SessionStore) async {
        await mutate(session) { try await session.client.setPrimaryGoal(id: goal.id) }
    }

    func delete(_ goal: Goal, _ session: SessionStore) async {
        await mutate(session) { try await session.client.deleteGoal(id: goal.id) }
    }

    /// Run a change, then reload so the list reflects the server truth.
    private func mutate(_ session: SessionStore, _ action: @Sendable () async throws -> Void) async {
        busy = true
        defer { busy = false }
        do {
            try await action()
            goals = try await session.client.goals()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct GoalsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = GoalsStore()
    @State private var editing: GoalEditorTarget?

    var body: some View {
        List {
            if store.goals.isEmpty && !store.loading {
                Section {
                    ContentUnavailableView {
                        Label("No goals yet", systemImage: "target")
                    } description: {
                        Text("Set something worth saving for — a game, a gadget, a day out. You'll see how close you are every time you earn.")
                    } actions: {
                        Button {
                            editing = .new
                        } label: {
                            Text("Add a goal").font(.body.weight(.semibold))
                        }
                        .buttonStyle(.borderedProminent)
                    }
                }
                .listRowBackground(Color.clear)
            } else {
                Section {
                    ForEach(store.goals) { goal in
                        goalRow(goal)
                    }
                } footer: {
                    Text("The primary goal is the one shown on Home and Earnings.")
                }
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(DB.help(scheme))
                }
            }
        }
        .navigationTitle("Goals")
        .graphiteBackground()
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    editing = .new
                } label: {
                    Image(systemName: "plus")
                }
                .disabled(store.busy)
            }
        }
        .sheet(item: $editing) { target in
            GoalEditorSheet(target: target) {
                Task { await store.load(session) }
            }
        }
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
    }

    private func goalRow(_ goal: Goal) -> some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 6) {
                    Text(goal.name)
                        .font(.body.weight(.semibold))
                        .lineLimit(1)
                    if goal.isPrimary {
                        Text("Primary")
                            .font(.caption2.weight(.bold))
                            .foregroundStyle(DB.gold(scheme))
                            .padding(.horizontal, 7)
                            .padding(.vertical, 2)
                            .background(DB.gold(scheme).opacity(0.15), in: Capsule())
                    }
                    if goal.isCompleted {
                        Image(systemName: "checkmark.seal.fill")
                            .font(.caption)
                            .foregroundStyle(DB.success(scheme))
                    }
                }
                ProgressView(value: Double(min(100, max(0, goal.progressPercent))), total: 100)
                    .tint(goal.isCompleted ? DB.success(scheme) : Color.accentColor)
                HStack {
                    Text("\(goal.currentBalance.display) of \(goal.targetAmount.display)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Text("\(goal.progressPercent)%")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.secondary)
                }
            }

            Menu {
                if !goal.isPrimary && !goal.isCompleted {
                    Button {
                        Task { await store.setPrimary(goal, session) }
                    } label: {
                        Label("Make primary", systemImage: "star")
                    }
                }
                Button {
                    editing = .edit(goal)
                } label: {
                    Label("Edit", systemImage: "pencil")
                }
                Button(role: .destructive) {
                    Task { await store.delete(goal, session) }
                } label: {
                    Label("Delete", systemImage: "trash")
                }
            } label: {
                Image(systemName: "ellipsis.circle")
                    .foregroundStyle(.secondary)
            }
            .disabled(store.busy)
        }
        .padding(.vertical, 4)
    }
}

/// What the editor sheet is doing — making a new goal, or editing one.
enum GoalEditorTarget: Identifiable {
    case new
    case edit(Goal)

    var id: Int {
        switch self {
        case .new: return -1
        case .edit(let goal): return goal.id
        }
    }

    var existing: Goal? {
        if case .edit(let goal) = self { return goal }
        return nil
    }
}

/// Add or edit one savings goal. Money is entered the same way as everywhere
/// else — a plain dollar amount — and the target must be positive.
struct GoalEditorSheet: View {
    let target: GoalEditorTarget
    var onSaved: () -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var name: String
    @State private var targetText: String
    @State private var detail: String
    @State private var isPrimary: Bool
    @State private var saving = false
    @State private var errorMessage: String?

    init(target: GoalEditorTarget, onSaved: @escaping () -> Void) {
        self.target = target
        self.onSaved = onSaved
        let g = target.existing
        _name = State(initialValue: g?.name ?? "")
        _targetText = State(initialValue: g.map { $0.targetAmount.wireString } ?? "")
        _detail = State(initialValue: g?.description ?? "")
        _isPrimary = State(initialValue: g?.isPrimary ?? false)
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: target.existing == nil ? "New Goal" : "Edit Goal")

            ScrollView {
                VStack(spacing: 14) {
                    SheetCard(title: "What are you saving for?") {
                        TextField("Name", text: $name)
                            .textFieldStyle(.plain)
                            .sheetFieldBackground()
                        TextField("A note (optional)", text: $detail)
                            .textFieldStyle(.plain)
                            .sheetFieldBackground()
                    }

                    SheetCard(title: "Target") {
                        HStack(spacing: 6) {
                            Text("$")
                                .foregroundStyle(DB.gold(scheme))
                                .font(.body.weight(.semibold))
                            TextField("0.00", text: $targetText)
                                .textFieldStyle(.plain)
                                #if os(iOS)
                                .keyboardType(.decimalPad)
                                #endif
                        }
                        .sheetFieldBackground()

                        Toggle(isOn: $isPrimary) {
                            Text("Show this on Home & Earnings")
                                .font(.subheadline)
                        }
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
                canSave: canSave,
                onCancel: { dismiss() },
                onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 440, idealWidth: 480, minHeight: 420, idealHeight: 460)
        #endif
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }

    private var trimmedName: String {
        name.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// The target field parsed to a positive Decimal, or nil if invalid.
    private var targetDecimal: Decimal? {
        let trimmed = targetText
            .trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: ",", with: ".")
        guard !trimmed.isEmpty,
              let value = Decimal(string: trimmed, locale: Locale(identifier: "en_US_POSIX")),
              value > 0 else { return nil }
        return value
    }

    private var canSave: Bool {
        !trimmedName.isEmpty && targetDecimal != nil
    }

    private func save() async {
        guard let amount = targetDecimal, !trimmedName.isEmpty else {
            errorMessage = "Give it a name and a target above $0."
            return
        }
        saving = true
        defer { saving = false }
        errorMessage = nil

        let write = GoalWrite(
            name: trimmedName,
            description: detail.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : detail,
            targetAmount: Money(amount),
            priority: target.existing?.priority ?? 0,
            isPrimary: isPrimary)

        do {
            if let existing = target.existing {
                try await session.client.updateGoal(id: existing.id, write)
            } else {
                _ = try await session.client.createGoal(write)
            }
            Haptics.success()
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
