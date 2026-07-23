import SwiftUI
import DailyBreadKit

/// Parent authoring of achievements: list every badge (active + inactive),
/// toggle each on/off, edit it, or create a new one — including automatic
/// unlock conditions. Achievements are global to the server; on a single-family
/// setup that's simply "your family's badges."
@MainActor
@Observable
final class AchievementDefsStore {
    var defs: [AchievementDefinition] = []
    var loading = false
    var errorMessage: String?
    var busy = false

    func load(_ session: SessionStore) async {
        loading = defs.isEmpty
        defer { loading = false }
        do {
            defs = try await session.client.achievementDefinitions()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func toggle(_ def: AchievementDefinition, _ session: SessionStore) async {
        busy = true
        defer { busy = false }
        do {
            try await session.client.toggleAchievementActive(id: def.id)
            defs = try await session.client.achievementDefinitions()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct AchievementDefinitionsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = AchievementDefsStore()
    @State private var editing: AchievementDefinition?
    @State private var creating = false

    var body: some View {
        List {
            if store.defs.isEmpty && !store.loading {
                Section {
                    ContentUnavailableView("No achievements yet", systemImage: "trophy",
                                           description: Text("Create one to get started."))
                }
                .listRowBackground(Color.clear)
            } else {
                ForEach(store.defs) { def in
                    Section {
                        row(def)
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
        .navigationTitle("Achievements")
        .graphiteBackground()
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { creating = true } label: { Image(systemName: "plus") }
                    .disabled(store.busy)
            }
        }
        .sheet(item: $editing) { def in
            AchievementEditorSheet(existing: def) { Task { await store.load(session) } }
        }
        .sheet(isPresented: $creating) {
            AchievementEditorSheet(existing: nil) { Task { await store.load(session) } }
        }
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
    }

    private func row(_ def: AchievementDefinition) -> some View {
        HStack(spacing: 12) {
            Text(def.icon.isEmpty ? "🏆" : def.icon)
                .font(.title2)
                .opacity(def.isActive ? 1 : 0.4)

            VStack(alignment: .leading, spacing: 3) {
                Text(def.name)
                    .font(.body.weight(.semibold))
                    .foregroundStyle(def.isActive ? .primary : .secondary)
                    .lineLimit(1)
                HStack(spacing: 6) {
                    Text(def.rarityKind.label)
                        .foregroundStyle(.secondary)
                    Text("· \(def.points) pts")
                        .foregroundStyle(.secondary)
                    if def.hasCashReward {
                        Text("· \(def.rewardCashAmount.display)")
                            .foregroundStyle(DB.gold(scheme))
                    } else if def.hasItemReward {
                        Label("Item", systemImage: "gift").foregroundStyle(.secondary)
                    }
                }
                .font(.caption)
                Text(def.unlock.label)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            Spacer()

            Toggle("", isOn: Binding(
                get: { def.isActive },
                set: { _ in Task { await store.toggle(def, session) } }))
                .labelsHidden()
                .disabled(store.busy)
        }
        .contentShape(Rectangle())
        .onTapGesture { editing = def }
        .padding(.vertical, 4)
    }
}

/// The full authoring form — create or edit one achievement, condition and all.
struct AchievementEditorSheet: View {
    let existing: AchievementDefinition?
    var onSaved: () -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    enum RewardKind: String, CaseIterable, Identifiable { case none = "None", cash = "Cash", item = "Item"; var id: String { rawValue } }

    @State private var name: String
    @State private var icon: String
    @State private var detail: String
    @State private var hiddenHint: String
    @State private var category: AchievementCategoryKind
    @State private var rarity: AchievementRarityKind
    @State private var points: Int
    @State private var condition: UnlockCondition
    @State private var countText: String
    @State private var daysText: String
    @State private var weeksText: String
    @State private var amountText: String
    @State private var beforeHour: Int
    @State private var dayType: String
    @State private var choreId: Int?
    @State private var isHidden: Bool
    @State private var isVisibleBeforeUnlock: Bool
    @State private var isLegendary: Bool
    @State private var isActive: Bool
    @State private var rewardKind: RewardKind
    @State private var rewardCashText: String
    @State private var rewardItemLabel: String
    @State private var rewardItemEstText: String
    @State private var advancedOpen = false
    @State private var saving = false
    @State private var errorMessage: String?
    @State private var chores: [PlannerChore] = []

    init(existing: AchievementDefinition?, onSaved: @escaping () -> Void) {
        self.existing = existing
        self.onSaved = onSaved
        _name = State(initialValue: existing?.name ?? "")
        _icon = State(initialValue: existing?.icon ?? "🏆")
        _detail = State(initialValue: existing?.description ?? "")
        _hiddenHint = State(initialValue: existing?.hiddenHint ?? "")
        _category = State(initialValue: existing?.categoryKind ?? .special)
        _rarity = State(initialValue: existing?.rarityKind ?? .common)
        _points = State(initialValue: existing?.points ?? 10)
        _condition = State(initialValue: existing?.unlock ?? .manual)
        _countText = State(initialValue: existing?.count.map(String.init) ?? "1")
        _daysText = State(initialValue: existing?.days.map(String.init) ?? "7")
        _weeksText = State(initialValue: existing?.weeks.map(String.init) ?? "4")
        _amountText = State(initialValue: (existing.map { !$0.conditionAmount.isZero } ?? false) ? existing!.conditionAmount.wireString : "")
        _beforeHour = State(initialValue: existing?.beforeHour ?? 12)
        _dayType = State(initialValue: existing?.dayType ?? "Weekend")
        _choreId = State(initialValue: existing?.choreId)
        _isHidden = State(initialValue: existing?.isHidden ?? false)
        _isVisibleBeforeUnlock = State(initialValue: existing?.isVisibleBeforeUnlock ?? true)
        _isLegendary = State(initialValue: existing?.isLegendary ?? false)
        _isActive = State(initialValue: existing?.isActive ?? true)
        let rk: RewardKind = existing?.hasCashReward == true ? .cash : (existing?.hasItemReward == true ? .item : .none)
        _rewardKind = State(initialValue: rk)
        _rewardCashText = State(initialValue: (existing?.hasCashReward ?? false) ? existing!.rewardCashAmount.wireString : "")
        _rewardItemLabel = State(initialValue: existing?.rewardItemLabel ?? "")
        _rewardItemEstText = State(initialValue: (existing.map { !$0.rewardItemEstValue.isZero } ?? false) ? existing!.rewardItemEstValue.wireString : "")
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: existing == nil ? "New Achievement" : "Edit Achievement")
            ScrollView {
                VStack(spacing: 14) {
                    basicsCard
                    conditionCard
                    rewardCard
                    advancedCard
                    if let errorMessage {
                        Label(errorMessage, systemImage: "exclamationmark.circle")
                            .font(.footnote).foregroundStyle(DB.help(scheme))
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                }
                .padding(.horizontal).padding(.top, 4).padding(.bottom, 12)
            }
            SheetActionBar(saveTitle: "Save", saving: saving, canSave: canSave,
                           onCancel: { dismiss() }, onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 480, idealWidth: 520, minHeight: 620, idealHeight: 720)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
        .task {
            if chores.isEmpty {
                chores = (try? await session.client.plannerChores().chores) ?? []
            }
        }
    }

    private var basicsCard: some View {
        SheetCard(title: "Basics") {
            HStack(spacing: 10) {
                TextField("🏆", text: $icon)
                    .multilineTextAlignment(.center)
                    .frame(width: 54)
                    .sheetFieldBackground()
                TextField("Name", text: $name)
                    .sheetFieldBackground()
            }
            TextField("Description", text: $detail, axis: .vertical)
                .lineLimit(1...3)
                .sheetFieldBackground()
            HStack(spacing: 12) {
                Picker("Rarity", selection: $rarity) {
                    ForEach(AchievementRarityKind.allCases) { Text($0.label).tag($0) }
                }
                Picker("Category", selection: $category) {
                    ForEach(AchievementCategoryKind.allCases) { Text($0.label).tag($0) }
                }
            }
            .pickerStyle(.menu)
            Stepper("Points: \(points)", value: $points, in: 0...1000, step: 5)
        }
    }

    private var conditionCard: some View {
        SheetCard(title: "How it's earned") {
            Picker("Condition", selection: $condition) {
                ForEach(UnlockCondition.allCases) { Text($0.label).tag($0) }
            }
            .pickerStyle(.menu)

            ForEach(condition.params, id: \.self) { param in
                conditionField(param)
            }

            if condition.isManual {
                Text("You award this one by hand — no automatic trigger.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private func conditionField(_ param: ConditionParam) -> some View {
        switch param {
        case .count:
            labeledField("How many", $countText, keyboard: true)
        case .days:
            labeledField("How many days", $daysText, keyboard: true)
        case .weeks:
            labeledField("How many weeks", $weeksText, keyboard: true)
        case .amount:
            moneyField("Dollar amount", $amountText)
        case .beforeHour:
            Picker("Before", selection: $beforeHour) {
                ForEach(1...23, id: \.self) { Text(hourLabel($0)).tag($0) }
            }
            .pickerStyle(.menu)
        case .dayType:
            Picker("Day type", selection: $dayType) {
                Text("Weekend").tag("Weekend")
                Text("Weekday").tag("Weekday")
            }
            .pickerStyle(.segmented)
        case .choreId:
            Picker("Which chore", selection: Binding(get: { choreId ?? chores.first?.id ?? 0 },
                                                     set: { choreId = $0 })) {
                ForEach(chores) { chore in Text(chore.name).tag(chore.id) }
            }
            .pickerStyle(.menu)
        }
    }

    private var rewardCard: some View {
        SheetCard(title: "Reward") {
            Picker("Reward", selection: $rewardKind) {
                ForEach(RewardKind.allCases) { Text($0.rawValue).tag($0) }
            }
            .pickerStyle(.segmented)

            switch rewardKind {
            case .none:
                Text("Just the badge and its points.")
                    .font(.caption).foregroundStyle(.secondary)
            case .cash:
                moneyField("Cash amount", $rewardCashText)
                Text("Credited to the child once you approve the claim.")
                    .font(.caption).foregroundStyle(.secondary)
            case .item:
                TextField("Item name", text: $rewardItemLabel).sheetFieldBackground()
                moneyField("Your estimate (optional)", $rewardItemEstText)
                Text("You fulfill this one in real life after approving.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var advancedCard: some View {
        SheetCard {
            DisclosureGroup(isExpanded: $advancedOpen) {
                VStack(spacing: 12) {
                    Toggle("Active", isOn: $isActive)
                    Toggle("Hidden until earned", isOn: $isHidden)
                    Toggle("Show a locked placeholder", isOn: $isVisibleBeforeUnlock)
                    Toggle("Legendary flourish", isOn: $isLegendary)
                    if isHidden {
                        TextField("Hint (shown while hidden)", text: $hiddenHint)
                            .sheetFieldBackground()
                    }
                }
                .padding(.top, 10)
            } label: {
                Text("More options").font(.subheadline.weight(.medium))
            }
            .tint(Color.accentColor)
        }
    }

    // MARK: - Small field helpers

    private func labeledField(_ label: String, _ text: Binding<String>, keyboard: Bool) -> some View {
        SheetField(label: label) {
            TextField("", text: text)
                #if os(iOS)
                .keyboardType(.numberPad)
                #endif
                .sheetFieldBackground()
        }
    }

    private func moneyField(_ label: String, _ text: Binding<String>) -> some View {
        SheetField(label: label) {
            HStack(spacing: 6) {
                Text("$").foregroundStyle(DB.gold(scheme)).font(.body.weight(.semibold))
                TextField("0.00", text: text)
                    #if os(iOS)
                    .keyboardType(.decimalPad)
                    #endif
            }
            .sheetFieldBackground()
        }
    }

    private func hourLabel(_ h: Int) -> String {
        var comps = DateComponents(); comps.hour = h
        let date = Calendar.current.date(from: comps) ?? Date()
        return date.formatted(.dateTime.hour())
    }

    // MARK: - Save

    private var canSave: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && !icon.trimmingCharacters(in: .whitespaces).isEmpty
            && !detail.trimmingCharacters(in: .whitespaces).isEmpty
    }

    private func decimalValue(_ text: String) -> Decimal? {
        let t = text.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: ",", with: ".")
        guard !t.isEmpty, let v = Decimal(string: t, locale: Locale(identifier: "en_US_POSIX")) else { return nil }
        return v
    }

    private func save() async {
        let params = condition.params
        let write = AchievementDefinitionWrite(
            name: name.trimmingCharacters(in: .whitespaces),
            description: detail.trimmingCharacters(in: .whitespaces),
            hiddenHint: hiddenHint.trimmingCharacters(in: .whitespaces).isEmpty ? nil : hiddenHint,
            icon: icon.trimmingCharacters(in: .whitespaces),
            lockedIcon: existing?.lockedIcon,
            category: category.rawValue,
            rarity: rarity.rawValue,
            points: points,
            sortOrder: existing?.sortOrder ?? 0,
            isHidden: isHidden,
            isLegendary: isLegendary,
            isVisibleBeforeUnlock: isVisibleBeforeUnlock,
            isActive: isActive,
            unlockConditionType: condition.rawValue,
            count: params.contains(.count) ? (Int(countText) ?? 1) : nil,
            days: params.contains(.days) ? (Int(daysText) ?? 1) : nil,
            weeks: params.contains(.weeks) ? (Int(weeksText) ?? 1) : nil,
            conditionAmount: params.contains(.amount) ? Money(decimalValue(amountText) ?? 0) : .zero,
            choreId: params.contains(.choreId) ? (choreId ?? chores.first?.id) : nil,
            beforeHour: params.contains(.beforeHour) ? beforeHour : nil,
            dayType: params.contains(.dayType) ? dayType : nil,
            progressTarget: nil,
            rewardType: rewardKind == .none ? nil : rewardKind.rawValue,
            rewardCashAmount: rewardKind == .cash ? Money(decimalValue(rewardCashText) ?? 0) : .zero,
            rewardItemLabel: rewardKind == .item ? rewardItemLabel.trimmingCharacters(in: .whitespaces) : nil,
            rewardItemEstValue: rewardKind == .item ? Money(decimalValue(rewardItemEstText) ?? 0) : .zero)

        // Light client-side guards; the server validates too.
        if rewardKind == .cash && (decimalValue(rewardCashText) ?? 0) <= 0 {
            errorMessage = "Cash reward needs an amount above $0."; return
        }
        if rewardKind == .item && write.rewardItemLabel?.isEmpty != false {
            errorMessage = "Give the item a name."; return
        }

        saving = true
        defer { saving = false }
        errorMessage = nil
        do {
            if let existing {
                try await session.client.updateAchievementDefinition(id: existing.id, write)
            } else {
                _ = try await session.client.createAchievementDefinition(write)
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
