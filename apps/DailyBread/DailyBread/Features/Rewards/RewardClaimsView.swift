import SwiftUI
import DailyBreadKit

/// Real-world reward claims from achievements. Two audiences, one screen:
/// a parent sees the household's pending claims and approves or rejects them;
/// a child sees their own claims and where each stands. Child names only appear
/// when there's more than one child — with a single child, there's no "whose."
enum RewardClaimsMode {
    case parent   // pending queue, with approve / reject
    case child    // own claims, read-only status
}

@MainActor
@Observable
final class RewardClaimsStore {
    var claims: [RewardClaim] = []
    var loading = false
    var errorMessage: String?
    var busy = false

    private var mode: RewardClaimsMode = .parent

    func load(_ session: SessionStore, mode: RewardClaimsMode) async {
        self.mode = mode
        loading = claims.isEmpty
        defer { loading = false }
        do {
            claims = mode == .parent
                ? try await session.client.pendingRewardClaims()
                : try await session.client.rewardClaims()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func approve(_ claim: RewardClaim, _ session: SessionStore) async {
        await mutate(session) { try await session.client.approveRewardClaim(id: claim.id) }
    }

    func reject(_ claim: RewardClaim, reason: String?, _ session: SessionStore) async {
        let trimmed = reason?.trimmingCharacters(in: .whitespacesAndNewlines)
        await mutate(session) {
            try await session.client.rejectRewardClaim(
                id: claim.id,
                reason: (trimmed?.isEmpty ?? true) ? nil : trimmed)
        }
    }

    private func mutate(_ session: SessionStore, _ action: @Sendable () async throws -> Void) async {
        busy = true
        defer { busy = false }
        do {
            try await action()
            Haptics.success()
            claims = mode == .parent
                ? try await session.client.pendingRewardClaims()
                : try await session.client.rewardClaims()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}

struct RewardClaimsView: View {
    var mode: RewardClaimsMode
    var title: String

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = RewardClaimsStore()
    @State private var rejecting: RewardClaim?

    private var showChildName: Bool { session.children.count > 1 }

    var body: some View {
        List {
            if store.claims.isEmpty && !store.loading {
                Section {
                    emptyState
                }
                .listRowBackground(Color.clear)
            } else {
                ForEach(store.claims) { claim in
                    Section {
                        claimRow(claim)
                    }
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
        .navigationTitle(title)
        .graphiteBackground()
        .refreshable { await store.load(session, mode: mode) }
        .refreshOnForeground { await store.load(session, mode: mode) }
        .task { await store.load(session, mode: mode) }
        .sheet(item: $rejecting) { claim in
            RejectReasonSheet(claim: claim) { reason in
                Task { await store.reject(claim, reason: reason, session) }
            }
        }
    }

    // MARK: - Row

    private func claimRow(_ claim: RewardClaim) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                Text(claim.achievementIcon.isEmpty ? "🏆" : claim.achievementIcon)
                    .font(.title2)
                VStack(alignment: .leading, spacing: 2) {
                    Text(rewardTitle(claim))
                        .font(.body.weight(.semibold))
                    Text(subtitle(claim))
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                rewardBadge(claim)
            }

            if mode == .child {
                statusLine(claim)
            }

            if let reason = claim.rejectionReason, !reason.isEmpty, claim.isRejected {
                Text("“\(reason)”")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .italic()
            }

            if mode == .parent && claim.isPending {
                HStack(spacing: 10) {
                    Button {
                        rejecting = claim
                    } label: {
                        Text("Decline")
                            .font(.subheadline.weight(.medium))
                            .frame(maxWidth: .infinity, minHeight: 38)
                    }
                    .buttonStyle(.plain)
                    .background(.quaternary.opacity(0.5),
                                in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .disabled(store.busy)

                    Button {
                        Task { await store.approve(claim, session) }
                    } label: {
                        Text(claim.isCash ? "Approve \(claim.cashAmount.display)" : "Mark given")
                            .font(.subheadline.weight(.semibold))
                            .frame(maxWidth: .infinity, minHeight: 38)
                            .foregroundStyle(.white)
                    }
                    .buttonStyle(.plain)
                    .background(Color.accentColor,
                                in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .disabled(store.busy)
                }
            }
        }
        .padding(.vertical, 4)
    }

    /// The reward itself is the headline: the cash, or the item's name.
    private func rewardTitle(_ claim: RewardClaim) -> String {
        if claim.isCash { return claim.cashAmount.display }
        return claim.itemLabel ?? "Reward"
    }

    private func subtitle(_ claim: RewardClaim) -> String {
        var parts: [String] = []
        if showChildName { parts.append(claim.childName) }
        parts.append("from “\(claim.achievementName)”")
        return parts.joined(separator: " · ")
    }

    private func rewardBadge(_ claim: RewardClaim) -> some View {
        Group {
            if claim.isCash {
                Text(claim.cashAmount.display)
                    .foregroundStyle(DB.gold(scheme))
            } else {
                Label("Item", systemImage: "gift")
                    .foregroundStyle(.secondary)
            }
        }
        .font(.subheadline.weight(.bold))
    }

    @ViewBuilder
    private func statusLine(_ claim: RewardClaim) -> some View {
        if claim.isPending {
            label("Waiting on \(session.voice.parents)", "hourglass", .secondary)
        } else if claim.isApproved {
            label(claim.isCash ? "Approved — added to your balance" : "Approved — coming your way",
                  "checkmark.seal.fill", DB.success(scheme))
        } else if claim.isRejected {
            label("Not this time", "xmark.circle", DB.help(scheme))
        }
    }

    private func label(_ text: String, _ icon: String, _ color: Color) -> some View {
        Label(text, systemImage: icon)
            .font(.caption.weight(.semibold))
            .foregroundStyle(color)
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label(mode == .parent ? "Nothing waiting" : "No rewards yet",
                  systemImage: mode == .parent ? "checkmark.circle" : "gift")
        } description: {
            Text(mode == .parent
                 ? "When a reward is earned, it'll show up here for you to approve."
                 : "Earn a reward achievement and you'll see it here — and where it stands.")
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 20)
    }
}

/// A small sheet to decline a claim with an optional, gentle reason the child sees.
private struct RejectReasonSheet: View {
    let claim: RewardClaim
    var onDecline: (String?) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var reason = ""

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Decline reward")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard(title: "Reason (optional)") {
                        TextField("A short note they'll see", text: $reason, axis: .vertical)
                            .textFieldStyle(.plain)
                            .lineLimit(2...4)
                            .sheetFieldBackground()
                        Text("Leave it blank to simply decline.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.horizontal)
                .padding(.top, 4)
            }
            SheetActionBar(
                saveTitle: "Decline",
                saving: false,
                canSave: true,
                onCancel: { dismiss() },
                onSave: {
                    onDecline(reason)
                    dismiss()
                })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 320, idealHeight: 340)
        #endif
        #if os(iOS)
        .presentationDetents([.medium])
        #endif
    }
}
