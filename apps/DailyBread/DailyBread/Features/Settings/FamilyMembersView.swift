import SwiftUI
import DailyBreadKit

/// The people in your household, and the access controls a parent needs:
/// reset a password, lock or unlock an account. It lists who's actually here —
/// nothing more. Adding, removing, and role changes live elsewhere by design.
@MainActor
@Observable
final class FamilyMembersStore {
    var members: [FamilyMember] = []
    var loading = false
    var errorMessage: String?
    var busy = false

    func load(_ session: SessionStore) async {
        loading = members.isEmpty
        defer { loading = false }
        do {
            members = try await session.client.familyMembers()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func setLocked(_ member: FamilyMember, locked: Bool, _ session: SessionStore) async {
        await mutate(session) {
            if locked { try await session.client.lockMember(userId: member.id) }
            else { try await session.client.unlockMember(userId: member.id) }
        }
    }

    private func mutate(_ session: SessionStore, _ action: @Sendable () async throws -> Void) async {
        busy = true
        defer { busy = false }
        do {
            try await action()
            members = try await session.client.familyMembers()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct FamilyMembersView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = FamilyMembersStore()
    @State private var resetting: FamilyMember?

    var body: some View {
        List {
            Section {
                ForEach(store.members) { member in
                    row(member)
                }
            } footer: {
                Text("Reset a password if someone's locked out, or pause an account with lock.")
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote).foregroundStyle(DB.help(scheme))
                }
            }
        }
        .navigationTitle("Family")
        .graphiteBackground()
        .sheet(item: $resetting) { member in
            ResetPasswordSheet(member: member) { Task { await store.load(session) } }
        }
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
    }

    private func row(_ member: FamilyMember) -> some View {
        let isSelf = member.id == session.currentUser?.userId
        return HStack(spacing: 12) {
            Circle()
                .fill(member.isParent ? Color.accentColor.gradient : DB.gold(scheme).gradient)
                .frame(width: 38, height: 38)
                .overlay {
                    Text(String(member.userName.prefix(1)).uppercased())
                        .font(.subheadline.weight(.bold)).foregroundStyle(.white)
                }

            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(member.userName).font(.body.weight(.medium))
                    if isSelf {
                        Text("You").font(.caption2.weight(.bold)).foregroundStyle(.secondary)
                    }
                }
                HStack(spacing: 6) {
                    Text(member.roleLabel).font(.caption).foregroundStyle(.secondary)
                    if member.isLockedOut {
                        Label("Locked", systemImage: "lock.fill")
                            .font(.caption2.weight(.semibold)).foregroundStyle(DB.help(scheme))
                    }
                }
            }

            Spacer()

            Menu {
                Button {
                    resetting = member
                } label: {
                    Label("Reset password", systemImage: "key")
                }
                if !isSelf {
                    if member.isLockedOut {
                        Button {
                            Task { await store.setLocked(member, locked: false, session) }
                        } label: {
                            Label("Unlock", systemImage: "lock.open")
                        }
                    } else {
                        Button(role: .destructive) {
                            Task { await store.setLocked(member, locked: true, session) }
                        } label: {
                            Label("Lock account", systemImage: "lock")
                        }
                    }
                }
            } label: {
                Image(systemName: "ellipsis.circle").foregroundStyle(.secondary)
            }
            .disabled(store.busy)
        }
        .padding(.vertical, 4)
    }
}

/// Set a new password for a household member.
private struct ResetPasswordSheet: View {
    let member: FamilyMember
    var onSaved: () -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var password = ""
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Reset \(member.userName)'s password")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard(title: "New password") {
                        SecureField("New password", text: $password)
                            .textContentType(.newPassword)
                            .sheetFieldBackground()
                        Text("They'll use this the next time they sign in.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote).foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal).padding(.top, 4)
            }
            SheetActionBar(saveTitle: "Set password", saving: saving,
                           canSave: password.count >= 4,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
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

    private func save() async {
        saving = true
        defer { saving = false }
        errorMessage = nil
        do {
            try await session.client.resetMemberPassword(userId: member.id, newPassword: password)
            Haptics.success()
            onSaved()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
