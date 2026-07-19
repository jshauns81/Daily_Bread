import SwiftUI
import DailyBreadKit

/// The parent's queue: completed chores waiting for the gold moment, and
/// open Help requests. Approve = glow + success haptic.
@MainActor
@Observable
final class ApprovalsStore {
    var queue: ApprovalsQueue?
    var loading = false
    var errorMessage: String?
    var justApprovedId: Int?
    var helpTarget: HelpRequest?

    func load(_ session: SessionStore) async {
        loading = queue == nil
        defer { loading = false }
        do {
            queue = try await session.client.approvalsQueue()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func approve(_ item: ApprovalItem, _ session: SessionStore) async {
        do {
            try await session.client.approve(choreLogId: item.choreLogId)
            justApprovedId = item.choreLogId
            Haptics.success()
            try? await Task.sleep(for: .seconds(0.9))
            withAnimation(.snappy) {
                queue?.pendingApprovals.removeAll { $0.choreLogId == item.choreLogId }
                justApprovedId = nil
            }
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }

    func respond(_ request: HelpRequest, _ kind: HelpResponseKind, _ session: SessionStore) async {
        do {
            try await session.client.respondToHelp(choreLogId: request.choreLogId, response: kind)
            Haptics.success()
            withAnimation(.snappy) {
                queue?.helpRequests.removeAll { $0.choreLogId == request.choreLogId }
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct ApprovalsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = ApprovalsStore()

    /// Both queues share the screen: each shows a handful, with a
    /// "N more" row that expands inline. No thumb marathons.
    @State private var showAllHelp = false
    @State private var showAllApprovals = false
    private let previewCap = 3

    var body: some View {
        List {
            if let queue = store.queue {
                if queue.isEmpty {
                    ContentUnavailableView(
                        "All caught up",
                        systemImage: "checkmark.seal",
                        description: Text("Nothing needs your approval right now."))
                }

                if !queue.helpRequests.isEmpty {
                    Section("Help raised") {
                        let shown = showAllHelp
                            ? queue.helpRequests
                            : Array(queue.helpRequests.prefix(previewCap))
                        ForEach(shown) { request in
                            helpRow(request)
                        }
                        if queue.helpRequests.count > previewCap {
                            expandRow(
                                expanded: $showAllHelp,
                                moreCount: queue.helpRequests.count - previewCap,
                                label: "more help requests",
                                color: DB.help(scheme))
                        }
                    }
                }

                if !queue.pendingApprovals.isEmpty {
                    Section("Waiting for approval") {
                        let shown = showAllApprovals
                            ? queue.pendingApprovals
                            : Array(queue.pendingApprovals.prefix(previewCap))
                        ForEach(shown) { item in
                            approvalRow(item)
                        }
                        if queue.pendingApprovals.count > previewCap {
                            expandRow(
                                expanded: $showAllApprovals,
                                moreCount: queue.pendingApprovals.count - previewCap,
                                label: "more waiting",
                                color: DB.gold(scheme))
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
        .navigationTitle("Approvals")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
        .sheet(item: Binding(
            get: { store.helpTarget },
            set: { store.helpTarget = $0 })
        ) { request in
            HelpRespondSheet(request: request) { kind in
                Task { await store.respond(request, kind, session) }
            }
        }
    }

    private func approvalRow(_ item: ApprovalItem) -> some View {
        let isGlowing = store.justApprovedId == item.choreLogId
        return HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(item.choreName)
                    .font(.body.weight(.medium))
                Text(item.childName)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Text(item.earnValue.display)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(DB.gold(scheme))

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
        }
        .padding(.vertical, 2)
        .listRowBackground(
            isGlowing
                ? DB.glow(scheme).opacity(0.18)
                : nil)
        .animation(.easeOut(duration: 0.4), value: isGlowing)
    }

    /// "12 more help requests ⌄" — tap to expand inline, tap again to fold.
    private func expandRow(
        expanded: Binding<Bool>,
        moreCount: Int,
        label: String,
        color: Color
    ) -> some View {
        Button {
            withAnimation(.snappy) { expanded.wrappedValue.toggle() }
        } label: {
            HStack {
                Text(expanded.wrappedValue ? "Show fewer" : "\(moreCount) \(label)")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(color)
                Spacer()
                Image(systemName: expanded.wrappedValue ? "chevron.up" : "chevron.down")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(color)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    private func helpRow(_ request: HelpRequest) -> some View {
        Button {
            store.helpTarget = request
        } label: {
            HStack(spacing: 12) {
                Circle()
                    .fill(DB.help(scheme))
                    .frame(width: 8, height: 8)
                VStack(alignment: .leading, spacing: 2) {
                    Text(request.choreName)
                        .font(.body.weight(.medium))
                        .foregroundStyle(.primary)
                    Text(request.reason ?? "\(request.childName) needs a hand")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                }
                Spacer()
                Image(systemName: "chevron.right")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
        }
        .buttonStyle(.plain)
    }
}

/// The Help response moment, designed instead of a stock dialog: the kid's
/// words up top, three clear outcomes below. "I helped" wears the gold.
struct HelpRespondSheet: View {
    let request: HelpRequest
    var onChoose: (HelpResponseKind) -> Void

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        Circle()
                            .fill(DB.help(scheme))
                            .frame(width: 9, height: 9)
                        Text("HELP RAISED")
                            .font(.caption.weight(.heavy))
                            .foregroundStyle(DB.help(scheme))
                            .kerning(1)
                    }
                    Text(request.choreName)
                        .font(.title2.weight(.bold))
                    Text("\(request.childName) · \(request.date.longDisplay)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }

                if let reason = request.reason, !reason.isEmpty {
                    Text("“\(reason)”")
                        .font(.body.italic())
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .glassCard()
                }

                Spacer()

                // Same vocabulary as the web app — the family's language.
                VStack(spacing: 10) {
                    choiceButton(
                        .completedByParent,
                        title: "✓ Fulfill for them",
                        subtitle: "You did it — \(request.childName) receives full credit",
                        prominent: true)
                    choiceButton(
                        .excused,
                        title: "Grant dispensation",
                        subtitle: "Excused for today. No penalty, no earning",
                        prominent: false)
                    choiceButton(
                        .denied,
                        title: "↺ They must try again",
                        subtitle: "Back to pending — \(request.childName) does it themselves",
                        prominent: false)
                }
            }
            .padding()
            .navigationTitle("Help")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
        }
        .graphiteBackground()
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }

    private func choiceButton(
        _ kind: HelpResponseKind,
        title: String,
        subtitle: String,
        prominent: Bool
    ) -> some View {
        Button {
            onChoose(kind)
            dismiss()
        } label: {
            VStack(spacing: 2) {
                Text(title)
                    .font(.body.weight(.bold))
                Text(subtitle)
                    .font(.caption)
                    .opacity(0.8)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 6)
        }
        .buttonStyle(.borderedProminent)
        .tint(prominent ? DB.gold(scheme) : Color.secondary.opacity(0.35))
        .foregroundStyle(prominent ? Color.black.opacity(0.8) : Color.primary)
    }
}
