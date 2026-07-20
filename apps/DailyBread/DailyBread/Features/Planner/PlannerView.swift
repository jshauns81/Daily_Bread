import SwiftUI
import DailyBreadKit

/// The parent's planner: every chore in the family, in the kid's list
/// order. Drag to reorder, swipe to deactivate or delete, tap to edit,
/// + to add. One server-ordered list; the child filter is only a view
/// over it — reordering stays global, so it's disabled while filtered.
/// All mutations are optimistic with rollback; errors show inline.
@MainActor
@Observable
final class PlannerStore {
    var chores: [PlannerChore] = []
    var children: [AssignableChild] = []
    var showInactive = false
    /// nil = Everyone. The filter is a VIEW over the one server-ordered
    /// list — the store never refetches per child.
    var selectedChildId: String?
    var loading = false
    var errorMessage: String?

    /// The rows on screen: server order, optionally narrowed to one child.
    var visibleChores: [PlannerChore] {
        guard let id = selectedChildId else { return chores }
        return chores.filter { $0.assignedUserId == id }
    }

    /// Order is global. Reordering a filtered subset would scramble the
    /// rest, so dragging only works on Everyone.
    var canReorder: Bool { selectedChildId == nil }

    /// New chores land at the end of the list.
    var nextSortOrder: Int { (chores.map(\.sortOrder).max() ?? -1) + 1 }

    func load(_ session: SessionStore) async {
        loading = chores.isEmpty
        defer { loading = false }
        do {
            async let choresTask = session.client.plannerChores(includeInactive: showInactive)
            async let childrenTask = session.client.assignableChildren()
            chores = try await choresTask.chores
            if let assignable = try? await childrenTask {
                children = assignable.children
            }
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Optimistic drag-reorder: move locally, renumber the FULL visible
    /// order contiguously (0, 1, 2, …), PUT it, roll back on failure.
    /// Only callable on Everyone, where visible == the whole list.
    func move(from source: IndexSet, to destination: Int, _ session: SessionStore) {
        guard canReorder else { return }
        let original = chores
        chores.move(fromOffsets: source, toOffset: destination)
        for index in chores.indices {
            chores[index].sortOrder = index
        }
        let items = chores.map { ChoreOrderItem(choreDefinitionId: $0.id, sortOrder: $0.sortOrder) }
        Haptics.tick()

        Task {
            do {
                try await session.client.reorderChores(items)
                errorMessage = nil
            } catch {
                withAnimation(.snappy) { self.chores = original }
                errorMessage = error.localizedDescription
                Haptics.warning()
            }
        }
    }

    /// Optimistic activate/deactivate: flip locally, POST, then adopt the
    /// server's fresh chore. Rolls back on failure. A chore deactivated
    /// while "Show inactive" is off slides out of the list.
    func toggleActive(_ chore: PlannerChore, _ session: SessionStore) {
        guard let index = chores.firstIndex(where: { $0.id == chore.id }) else { return }
        let original = chores[index]
        withAnimation(.snappy) { chores[index].isActive.toggle() }
        Haptics.tick()

        Task {
            do {
                let fresh = try await session.client.toggleChoreActive(id: chore.id)
                if let i = self.chores.firstIndex(where: { $0.id == chore.id }) {
                    withAnimation(.snappy) {
                        if !self.showInactive && !fresh.isActive {
                            _ = self.chores.remove(at: i)
                        } else {
                            self.chores[i] = fresh
                        }
                    }
                }
                errorMessage = nil
            } catch {
                if let i = self.chores.firstIndex(where: { $0.id == chore.id }) {
                    withAnimation(.snappy) { self.chores[i] = original }
                }
                errorMessage = error.localizedDescription
                Haptics.warning()
            }
        }
    }

    /// Optimistic delete: remove locally, DELETE, reinsert on failure.
    /// (Servers soft-delete to inactive when history exists — either way
    /// the chore leaves this list; a refresh settles any difference.)
    func delete(_ chore: PlannerChore, _ session: SessionStore) {
        guard let index = chores.firstIndex(where: { $0.id == chore.id }) else { return }
        let removed = chores[index]
        _ = withAnimation(.snappy) { chores.remove(at: index) }
        Haptics.tick()

        Task {
            do {
                try await session.client.deleteChore(id: chore.id)
                errorMessage = nil
            } catch {
                withAnimation(.snappy) {
                    self.chores.insert(removed, at: min(index, self.chores.count))
                }
                errorMessage = error.localizedDescription
                Haptics.warning()
            }
        }
    }

    /// Adopt a chore fresh from the editor: replace in place, or append
    /// (new chores carry max sortOrder + 1, which is the end of the
    /// server order too).
    func upsert(_ fresh: PlannerChore) {
        withAnimation(.snappy) {
            if let index = chores.firstIndex(where: { $0.id == fresh.id }) {
                if !showInactive && !fresh.isActive {
                    _ = chores.remove(at: index)
                } else {
                    chores[index] = fresh
                }
            } else {
                chores.append(fresh)
            }
        }
    }
}

/// Sheet payload: the chore being edited, or nil for a brand-new one.
private struct ChoreEditorTarget: Identifiable {
    let chore: PlannerChore?

    var id: Int { chore?.id ?? -1 }
}

struct PlannerView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = PlannerStore()
    @State private var editorTarget: ChoreEditorTarget?
    /// The row currently morphed into its inline delete confirm.
    @State private var confirmingDeleteId: Int?

    var body: some View {
        List {
            if !store.children.isEmpty && !store.chores.isEmpty {
                Section {
                    childFilter
                        .listRowInsets(EdgeInsets(top: 4, leading: 16, bottom: 4, trailing: 16))
                        .listRowBackground(Color.clear)
                }
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }

            if !store.visibleChores.isEmpty {
                Section {
                    ForEach(store.visibleChores) { chore in
                        row(chore)
                    }
                    .onMove(perform: moveHandler)
                } footer: {
                    if !store.canReorder && store.chores.count > 1 {
                        Text("Show Everyone to reorder.")
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                }
            } else if store.loading {
                Section {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .listRowBackground(Color.clear)
                }
            } else if store.chores.isEmpty {
                Section {
                    emptyState
                        .listRowBackground(Color.clear)
                }
            } else {
                // A kid is selected and has nothing yet.
                Section {
                    Text("Nothing for \(selectedChildName) yet — add one with +.")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                        .listRowBackground(Color.clear)
                }
            }
        }
        .navigationTitle("Planner")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
        .onChange(of: store.showInactive) { _, _ in
            Task { await store.load(session) }
        }
        .onChange(of: store.selectedChildId) { _, _ in
            // A morphed confirm row shouldn't survive a filter change.
            confirmingDeleteId = nil
        }
        .toolbar {
            #if os(iOS)
            ToolbarItem(placement: .topBarLeading) {
                if store.canReorder && store.chores.count > 1 {
                    EditButton()
                }
            }
            #endif
            ToolbarItem(placement: .primaryAction) {
                Button {
                    Haptics.tick()
                    editorTarget = ChoreEditorTarget(chore: nil)
                } label: {
                    Label("Add chore", systemImage: "plus")
                }
            }
            ToolbarItem(placement: .automatic) {
                Menu {
                    Toggle("Show inactive", isOn: $store.showInactive)
                } label: {
                    Label("More", systemImage: "ellipsis.circle")
                }
            }
        }
        .sheet(item: $editorTarget) { target in
            ChoreEditorSheet(
                chore: target.chore,
                children: store.children,
                nextSortOrder: store.nextSortOrder
            ) { fresh in
                store.upsert(fresh)
            }
        }
    }

    /// Reorder only on Everyone — nil disables the drag affordance.
    private var moveHandler: ((IndexSet, Int) -> Void)? {
        guard store.canReorder else { return nil }
        return { source, destination in
            store.move(from: source, to: destination, session)
        }
    }

    private var selectedChildName: String {
        store.children.first(where: { $0.userId == store.selectedChildId })?.userName ?? "them"
    }

    // MARK: - Rows

    @ViewBuilder
    private func row(_ chore: PlannerChore) -> some View {
        if confirmingDeleteId == chore.id {
            DeleteConfirmRow(chore: chore) {
                confirmingDeleteId = nil
                store.delete(chore, session)
            } onKeep: {
                withAnimation(.snappy) { confirmingDeleteId = nil }
            }
        } else {
            PlannerChoreRow(chore: chore, showAssignee: store.selectedChildId == nil) {
                editorTarget = ChoreEditorTarget(chore: chore)
            }
            .swipeActions(edge: .leading, allowsFullSwipe: false) {
                Button(chore.isActive ? "Deactivate" : "Activate") {
                    store.toggleActive(chore, session)
                }
                .tint(Color.secondary)
            }
            .swipeActions(edge: .trailing, allowsFullSwipe: false) {
                // Red, but never full-swipe: delete goes through the
                // inline row-morph confirm, never a system dialog.
                Button("Delete") {
                    withAnimation(.snappy) { confirmingDeleteId = chore.id }
                }
                .tint(DB.help(scheme))
            }
        }
    }

    // MARK: - Child filter

    private var childFilter: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                filterChip("Everyone", id: nil)
                ForEach(store.children) { child in
                    filterChip(child.userName, id: child.userId)
                }
            }
        }
    }

    private func filterChip(_ title: String, id: String?) -> some View {
        let selected = store.selectedChildId == id
        return Button {
            Haptics.tick()
            withAnimation(.snappy) { store.selectedChildId = id }
        } label: {
            Text(title)
                .font(.subheadline.weight(.semibold))
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(
                    selected ? Color.accentColor : Color.secondary.opacity(0.13),
                    in: Capsule())
                .foregroundStyle(selected ? Color.white : Color.primary)
        }
        .buttonStyle(.plain)
    }

    // MARK: - Empty state

    private var emptyState: some View {
        ContentUnavailableView {
            Label("No chores yet", systemImage: "checklist")
        } description: {
            Text("Add the first one. Tasks earn money; Routines are just expected.")
        } actions: {
            Button {
                Haptics.tick()
                editorTarget = ChoreEditorTarget(chore: nil)
            } label: {
                Label("Add a chore", systemImage: "plus")
                    .font(.body.weight(.semibold))
            }
            .buttonStyle(.borderedProminent)
            .tint(Color.accentColor)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 24)
    }
}

/// One chore in the planner: icon, name (greyed + "Off" when inactive),
/// schedule caption, and on the right either the gold earn value (Task)
/// or a quiet "Routine" capsule — plus "📺 N" when it weighs on screen
/// time. Tap anywhere to edit.
private struct PlannerChoreRow: View {
    let chore: PlannerChore
    /// On Everyone, say whose chore each row is.
    let showAssignee: Bool
    var onTap: () -> Void

    @Environment(\.colorScheme) private var scheme

    var body: some View {
        Button(action: onTap) {
            HStack(spacing: 12) {
                Text(iconText)
                    .font(.title3)
                    .frame(width: 40, height: 40)
                    .background(.quaternary, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .opacity(chore.isActive ? 1 : 0.45)

                VStack(alignment: .leading, spacing: 2) {
                    HStack(spacing: 6) {
                        Text(chore.name)
                            .font(.body.weight(.medium))
                            .foregroundStyle(chore.isActive ? .primary : .secondary)
                        if !chore.isActive {
                            Text("Off")
                                .font(.caption2.weight(.heavy))
                                .padding(.horizontal, 7)
                                .padding(.vertical, 2)
                                .background(Color.secondary.opacity(0.15), in: Capsule())
                                .foregroundStyle(.secondary)
                        }
                    }
                    Text(caption)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                VStack(alignment: .trailing, spacing: 3) {
                    if chore.isTask {
                        Text(chore.earnValue.display)
                            .font(.subheadline.weight(.semibold))
                            .foregroundStyle(DB.gold(scheme).opacity(chore.isActive ? 1 : 0.5))
                    } else {
                        Text("Routine")
                            .font(.caption2.weight(.bold))
                            .padding(.horizontal, 8)
                            .padding(.vertical, 3)
                            .background(Color.secondary.opacity(0.13), in: Capsule())
                            .foregroundStyle(.secondary)
                    }
                    if chore.importance > 0 {
                        Text("📺 \(chore.importance)")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private var iconText: String {
        if let icon = chore.icon, !icon.isEmpty { return icon }
        return "🧺"
    }

    private var caption: String {
        var parts = [chore.scheduleSummary]
        if showAssignee {
            parts.append(chore.assignedUserName ?? "Anyone")
        }
        return parts.joined(separator: " · ")
    }
}

/// The delete confirm the row morphs into — inline, in place, no system
/// dialog. Red only because it's destructive.
private struct DeleteConfirmRow: View {
    let chore: PlannerChore
    var onDelete: () -> Void
    var onKeep: () -> Void

    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Delete “\(chore.name)”? This can't be undone.")
                .font(.subheadline.weight(.medium))
            HStack(spacing: 10) {
                Button("Keep", action: onKeep)
                    .buttonStyle(.bordered)
                    .tint(Color.secondary)
                Button("Delete", action: onDelete)
                    .buttonStyle(.borderedProminent)
                    .tint(DB.help(scheme))
            }
            .font(.subheadline.weight(.semibold))
        }
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .listRowBackground(DB.help(scheme).opacity(0.08))
    }
}
