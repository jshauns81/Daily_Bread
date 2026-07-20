import SwiftUI
import DailyBreadKit

/// Create or edit one chore. Tasks earn money; Routines are just
/// expected. Saving builds the full ChoreWrite (sortOrder preserved on
/// edit, appended on create), POSTs/PUTs, and hands the fresh chore back
/// through `onSaved`. Errors show inline (DB.help) — never a system
/// alert.
struct ChoreEditorSheet: View {
    /// nil = creating a new chore.
    let chore: PlannerChore?
    let children: [AssignableChild]
    /// Where a brand-new chore lands: the store's max sortOrder + 1.
    let nextSortOrder: Int
    var onSaved: (PlannerChore) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    @State private var name: String
    @State private var icon: String
    @State private var details: String
    /// "" = Anyone (unassigned).
    @State private var assignedUserId: String
    @State private var isTaskKind: Bool
    @State private var earnText: String
    /// false = SpecificDays, true = WeeklyFrequency.
    @State private var isWeekly: Bool
    /// Full day names ("Sunday" … "Saturday").
    @State private var selectedDays: Set<String>
    @State private var weeklyTarget: Int
    @State private var isRepeatable: Bool
    @State private var allOrNothing: Bool
    @State private var importance: Double
    @State private var autoApprove: Bool
    @State private var isActive: Bool
    @State private var saving = false
    @State private var errorMessage: String?

    private struct DayOption: Identifiable {
        let full: String
        let chip: String
        var id: String { full }
    }

    /// Sunday-first, matching the web planner's S M T W T F S chips.
    private static let week: [DayOption] = [
        DayOption(full: "Sunday", chip: "S"),
        DayOption(full: "Monday", chip: "M"),
        DayOption(full: "Tuesday", chip: "T"),
        DayOption(full: "Wednesday", chip: "W"),
        DayOption(full: "Thursday", chip: "T"),
        DayOption(full: "Friday", chip: "F"),
        DayOption(full: "Saturday", chip: "S"),
    ]

    init(chore: PlannerChore?,
         children: [AssignableChild],
         nextSortOrder: Int,
         onSaved: @escaping (PlannerChore) -> Void) {
        self.chore = chore
        self.children = children
        self.nextSortOrder = nextSortOrder
        self.onSaved = onSaved

        _name = State(initialValue: chore?.name ?? "")
        _icon = State(initialValue: chore?.icon ?? "")
        _details = State(initialValue: chore?.description ?? "")
        _assignedUserId = State(initialValue: chore?.assignedUserId ?? "")
        _isTaskKind = State(initialValue: chore.map { $0.kind == "Task" } ?? true)
        _earnText = State(initialValue: {
            if let chore, chore.kind == "Task" { return chore.earnValue.wireString }
            return "1.00"
        }())
        _isWeekly = State(initialValue: chore?.scheduleType == "WeeklyFrequency")
        _selectedDays = State(initialValue: {
            if let chore, !chore.activeDays.isEmpty { return Set(chore.activeDays) }
            return Set(Self.week.map(\.full))
        }())
        _weeklyTarget = State(initialValue: min(7, max(1, chore?.weeklyTargetCount ?? 3)))
        _isRepeatable = State(initialValue: chore?.isRepeatable ?? false)
        _allOrNothing = State(initialValue: chore?.allOrNothing ?? false)
        _importance = State(initialValue: Double(chore?.importance ?? 0))
        _autoApprove = State(initialValue: chore?.autoApprove ?? true)
        _isActive = State(initialValue: chore?.isActive ?? true)
    }

    private var isEditing: Bool { chore != nil }

    private var title: String {
        let noun = isTaskKind ? "Task" : "Routine"
        return isEditing ? "Edit \(noun)" : "New \(noun)"
    }

    var body: some View {
        NavigationStack {
            Form {
                identitySection
                whoSection
                kindSection
                scheduleSection
                screenTimeSection
                behaviorSection
                saveSection
            }
            .navigationTitle(title)
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                        .disabled(saving)
                }
            }
        }
        .graphiteBackground()
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    // MARK: - Sections

    private var identitySection: some View {
        Section {
            HStack(spacing: 10) {
                TextField("🧺", text: $icon)
                    .frame(width: 52)
                    .multilineTextAlignment(.center)
                    .font(.title3)
                    .onChange(of: icon) { _, newValue in
                        // One emoji is plenty (two characters max).
                        if newValue.count > 2 { icon = String(newValue.prefix(2)) }
                    }
                TextField("What's the chore?", text: $name)
                    .font(.body.weight(.medium))
            }
            TextField("Notes (optional)", text: $details, axis: .vertical)
                .lineLimit(1...3)
        }
    }

    @ViewBuilder
    private var whoSection: some View {
        if !children.isEmpty {
            Section("Who") {
                Picker("For", selection: $assignedUserId) {
                    Text("Anyone").tag("")
                    ForEach(children) { child in
                        Text(child.userName).tag(child.userId)
                    }
                }
            }
        }
    }

    private var kindSection: some View {
        Section {
            Picker("Kind", selection: $isTaskKind) {
                Text("Earns money").tag(true)
                Text("Routine").tag(false)
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            if isTaskKind {
                HStack {
                    Text("$")
                        .foregroundStyle(DB.gold(scheme))
                    TextField("1.00", text: $earnText)
                        #if os(iOS)
                        .keyboardType(.decimalPad)
                        #endif
                }
            }
        } footer: {
            Text(isTaskKind
                 ? "Tasks earn money when they're done and approved."
                 : "Routines are just expected — no money attached.")
        }
    }

    private var scheduleSection: some View {
        Section {
            Picker("Schedule", selection: $isWeekly) {
                Text("Certain days").tag(false)
                Text("Times per week").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            if isWeekly {
                Stepper("\(weeklyTarget)× a week", value: $weeklyTarget, in: 1...7)
                    .monospacedDigit()
                Toggle("Extra reps allowed", isOn: $isRepeatable)
                if isTaskKind {
                    Toggle("All-or-nothing pay", isOn: $allOrNothing)
                }
            } else {
                dayChips
            }
        } header: {
            Text("Schedule")
        } footer: {
            if isWeekly {
                if isTaskKind && allOrNothing {
                    Text("All-or-nothing: the week pays only if every rep gets done.")
                }
            } else if selectedDays.isEmpty {
                Text("Pick at least one day.")
                    .foregroundStyle(DB.help(scheme))
            }
        }
    }

    private var dayChips: some View {
        HStack(spacing: 6) {
            ForEach(Self.week) { day in
                dayChip(day)
            }
        }
        .padding(.vertical, 4)
    }

    private func dayChip(_ day: DayOption) -> some View {
        let on = selectedDays.contains(day.full)
        return Button {
            Haptics.tick()
            withAnimation(.snappy) {
                if on {
                    selectedDays.remove(day.full)
                } else {
                    selectedDays.insert(day.full)
                }
            }
        } label: {
            Text(day.chip)
                .font(.subheadline.weight(.semibold))
                .frame(maxWidth: .infinity)
                .frame(height: 34)
                .background(
                    on ? Color.accentColor : Color.secondary.opacity(0.12),
                    in: RoundedRectangle(cornerRadius: 9, style: .continuous))
                .foregroundStyle(on ? Color.white : Color.primary)
        }
        .buttonStyle(.plain)
    }

    private var screenTimeSection: some View {
        Section {
            VStack(alignment: .leading, spacing: 6) {
                Text(importanceInt == 0
                     ? "No screen-time impact"
                     : "Importance \(importanceInt) of 10")
                    .font(.subheadline.weight(.medium))
                    .monospacedDigit()
                Slider(value: $importance, in: 0...10, step: 1)
                if importanceInt > 0 {
                    Text("Missing it costs a share of the weekly screen-time budget.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.vertical, 2)
        } header: {
            Text("Screen time")
        }
    }

    private var behaviorSection: some View {
        Section("Behavior") {
            Toggle(isOn: $autoApprove) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Approves itself")
                    Text("No parent check needed")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            if isEditing {
                Toggle("Active", isOn: $isActive)
            }
        }
    }

    private var saveSection: some View {
        Section {
            Button {
                Task { await save() }
            } label: {
                if saving {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                } else {
                    Text(isEditing ? "Save" : "Create")
                        .font(.body.weight(.semibold))
                        .frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.borderedProminent)
            .tint(Color.accentColor)
            .disabled(saving || !canSave)
            .listRowBackground(Color.clear)
            .listRowInsets(EdgeInsets())

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.circle")
                    .font(.footnote)
                    .foregroundStyle(DB.help(scheme))
                    .listRowBackground(Color.clear)
            }
        }
    }

    // MARK: - Validation / save

    private var importanceInt: Int { Int(importance) }

    /// The earn field parsed to a non-negative Decimal, or nil if invalid.
    /// Same rules as the screen-time payout field.
    private var earnDecimal: Decimal? {
        let trimmed = earnText
            .trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: ",", with: ".")
        guard !trimmed.isEmpty,
              let value = Decimal(string: trimmed, locale: Locale(identifier: "en_US_POSIX")),
              value >= 0 else { return nil }
        return value
    }

    private var canSave: Bool {
        !name.trimmingCharacters(in: .whitespaces).isEmpty
            && (!isTaskKind || earnDecimal != nil)
            && (isWeekly || !selectedDays.isEmpty)
    }

    /// Selected days as full names, Sunday-first. A weekly-goal chore
    /// with nothing picked falls back to every day (the server schedules
    /// within these).
    private var orderedActiveDays: [String] {
        let days = Self.week.map(\.full).filter { selectedDays.contains($0) }
        return days.isEmpty ? Self.week.map(\.full) : days
    }

    private func save() async {
        let trimmedName = name.trimmingCharacters(in: .whitespaces)
        guard canSave else { return }

        var earn = Money.zero
        if isTaskKind {
            guard let value = earnDecimal else {
                errorMessage = "Enter a valid dollar amount."
                return
            }
            earn = Money(value)
        }

        saving = true
        defer { saving = false }
        errorMessage = nil

        let trimmedIcon = icon.trimmingCharacters(in: .whitespaces)
        let trimmedDetails = details.trimmingCharacters(in: .whitespacesAndNewlines)

        let write = ChoreWrite(
            name: trimmedName,
            description: trimmedDetails.isEmpty ? nil : trimmedDetails,
            icon: trimmedIcon.isEmpty ? nil : String(trimmedIcon.prefix(2)),
            assignedUserId: assignedUserId.isEmpty ? nil : assignedUserId,
            kind: isTaskKind ? "Task" : "Routine",
            // The server zeroes Routine earn values anyway; send zero.
            earnValue: isTaskKind ? earn : .zero,
            importance: importanceInt,
            allOrNothing: allOrNothing,
            // Not editable here (later pass) — preserved on edit.
            isInverseFill: chore?.isInverseFill ?? false,
            inverseFillBaselineMinutes: chore?.inverseFillBaselineMinutes ?? 0,
            scheduleType: isWeekly ? "WeeklyFrequency" : "SpecificDays",
            activeDays: orderedActiveDays,
            weeklyTargetCount: weeklyTarget,
            isRepeatable: isWeekly ? isRepeatable : false,
            // Date windows aren't in the v1 editor — preserved on edit.
            startDate: chore?.startDate,
            endDate: chore?.endDate,
            isActive: isEditing ? isActive : true,
            autoApprove: autoApprove,
            sortOrder: chore?.sortOrder ?? nextSortOrder)

        do {
            let fresh: PlannerChore
            if let chore {
                fresh = try await session.client.updateChore(id: chore.id, write)
            } else {
                fresh = try await session.client.createChore(write)
            }
            Haptics.success()
            onSaved(fresh)
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
            Haptics.warning()
        }
    }
}
