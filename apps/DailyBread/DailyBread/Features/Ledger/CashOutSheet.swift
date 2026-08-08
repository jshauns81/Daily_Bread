import SwiftUI
import DailyBreadKit

struct CashOutTarget: Identifiable {
    /// Nil means self — a child recording their own cash-out.
    let userId: String?
    /// Possessive as shown in copy: "Victor's" / "your".
    let possessive: String
    let balance: Money
    let threshold: Money
    var id: String { userId ?? "self" }
}

/// Records that money left the ledger — bookkeeping only, and the sheet says
/// so out loud. The actual transfer happens in the family's banking app; this
/// writes the typed Payout line the history and stats report on.
struct CashOutSheet: View {
    let target: CashOutTarget
    var onSaved: (Money) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var amountText = ""
    @State private var note = ""
    @State private var saving = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Record a cash-out")
            ScrollView {
                VStack(spacing: 14) {
                    SheetCard {
                        HStack {
                            Text("Balance")
                            Spacer()
                            Text(target.balance.display)
                                .font(.body.weight(.semibold))
                                .foregroundStyle(DB.gold(scheme))
                        }
                    }

                    SheetCard(title: "Amount") {
                        HStack(spacing: 8) {
                            ForEach(QuickFill.allCases) { fill in
                                Button(fill.label) {
                                    amountText = fill.amount(of: target.balance).wireString
                                }
                                .font(.subheadline.weight(.semibold))
                                .buttonStyle(.bordered)
                            }
                        }

                        HStack(spacing: 6) {
                            Text("$")
                                .foregroundStyle(DB.gold(scheme))
                                .font(.body.weight(.semibold))
                            TextField("0.00", text: $amountText)
                                #if os(iOS)
                                .keyboardType(.decimalPad)
                                #endif
                        }
                        .sheetFieldBackground()

                        if let preview = remainingPreview {
                            Text("Leaves \(preview.display) in \(target.possessive) account.")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }

                    SheetCard(title: "Note") {
                        TextField("Optional — how it was paid, for the record",
                                  text: $note, axis: .vertical)
                            .lineLimit(1...3)
                            .sheetFieldBackground()
                    }

                    // The one thing this sheet must never let anyone believe:
                    // that tapping Record moved money anywhere.
                    Label("Bookkeeping only — move the actual money in your banking app.",
                          systemImage: "info.circle")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)

                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote).foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal).padding(.top, 4).padding(.bottom, 12)
            }
            SheetActionBar(saveTitle: "Record", saving: saving, canSave: canSave,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
                .padding()
        }
        .themeBackground()
        .parentGateHold()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 440, idealHeight: 480)
        #endif
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }

    /// The web modal's 25/50/75/All quick-fill, kept because a cash-out is
    /// nearly always "some round share of what's there".
    private enum QuickFill: CaseIterable, Identifiable {
        case quarter, half, threeQuarters, all
        var id: Self { self }
        var label: String {
            switch self {
            case .quarter: return "25%"
            case .half: return "50%"
            case .threeQuarters: return "75%"
            case .all: return "All"
            }
        }
        func amount(of balance: Money) -> Money {
            switch self {
            case .all: return balance
            case .quarter: return balance.share(0.25)
            case .half: return balance.share(0.5)
            case .threeQuarters: return balance.share(0.75)
            }
        }
    }

    private var amount: Decimal? {
        let t = amountText.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
        guard !t.isEmpty, let v = Decimal(string: t, locale: Locale(identifier: "en_US_POSIX")), v > 0 else { return nil }
        return v
    }

    private var remainingPreview: Money? {
        guard let amount, amount <= target.balance.amount else { return nil }
        return Money(target.balance.amount - amount)
    }

    private var canSave: Bool {
        guard let amount else { return false }
        return amount <= target.balance.amount
    }

    private func save() async {
        guard let amount else { errorMessage = "Enter an amount above $0."; return }
        saving = true
        defer { saving = false }
        errorMessage = nil
        let trimmedNote = note.trimmingCharacters(in: .whitespaces)
        do {
            let fresh = try await session.client.cashOut(
                userId: target.userId, amount: Money(amount),
                note: trimmedNote.isEmpty ? nil : trimmedNote)
            Haptics.success()
            onSaved(fresh.balance)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
