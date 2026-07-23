import SwiftUI
import DailyBreadKit

struct AdjustBalanceTarget: Identifiable {
    let userId: String
    let name: String
    let balance: Money
    var id: String { userId }
}

/// Parent add/subtract on a child's balance, with a required reason that's kept
/// on the ledger. A manual correction — always explained, never silent.
struct AdjustBalanceSheet: View {
    let target: AdjustBalanceTarget
    var onSaved: (Money) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var subtract = false
    @State private var amountText = ""
    @State private var reason = ""
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Adjust \(target.name)'s balance")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard {
                        HStack {
                            Text("Current balance")
                            Spacer()
                            Text(target.balance.display)
                                .font(.body.weight(.semibold))
                                .foregroundStyle(DB.gold(scheme))
                        }
                    }

                    SheetCard(title: "Change") {
                        Picker("", selection: $subtract) {
                            Text("Add").tag(false)
                            Text("Subtract").tag(true)
                        }
                        .pickerStyle(.segmented)

                        HStack(spacing: 6) {
                            Text(subtract ? "−$" : "+$")
                                .foregroundStyle(subtract ? DB.help(scheme) : DB.gold(scheme))
                                .font(.body.weight(.semibold))
                            TextField("0.00", text: $amountText)
                                #if os(iOS)
                                .keyboardType(.decimalPad)
                                #endif
                        }
                        .sheetFieldBackground()

                        if let preview = newBalancePreview {
                            Text("New balance: \(preview.display)")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }

                    SheetCard(title: "Reason") {
                        TextField("Why — kept on the record", text: $reason, axis: .vertical)
                            .lineLimit(1...3)
                            .sheetFieldBackground()
                    }

                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote).foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal).padding(.top, 4).padding(.bottom, 12)
            }
            SheetActionBar(saveTitle: "Apply", saving: saving, canSave: canSave,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 420, idealHeight: 460)
        #endif
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }

    private var amount: Decimal? {
        let t = amountText.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
        guard !t.isEmpty, let v = Decimal(string: t, locale: Locale(identifier: "en_US_POSIX")), v > 0 else { return nil }
        return v
    }

    private var newBalancePreview: Money? {
        guard let amount else { return nil }
        let delta = subtract ? -amount : amount
        return Money(target.balance.amount + delta)
    }

    private var canSave: Bool {
        amount != nil && !reason.trimmingCharacters(in: .whitespaces).isEmpty
    }

    private func save() async {
        guard let amount else { errorMessage = "Enter an amount above $0."; return }
        let trimmedReason = reason.trimmingCharacters(in: .whitespaces)
        guard !trimmedReason.isEmpty else { errorMessage = "A reason is required."; return }

        saving = true
        defer { saving = false }
        errorMessage = nil
        let signed = subtract ? -amount : amount
        do {
            let fresh = try await session.client.adjustBalance(
                userId: target.userId, amount: Money(signed), description: trimmedReason)
            Haptics.success()
            onSaved(fresh.balance)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
