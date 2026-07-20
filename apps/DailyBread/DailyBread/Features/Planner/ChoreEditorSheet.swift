import SwiftUI
import DailyBreadKit

/// Create or edit one chore. Earns = a paid Task; Expected = an unpaid Routine.
/// Laid out explicitly (not a macOS `Form`, which renders cramped) to match the
/// original planner's clean edit dialog: icon+name, Earns/Expected, a schedule
/// rule, then grouped options. Saving builds the full ChoreWrite and hands the
/// fresh chore back through `onSaved`. Errors show inline — never a system alert.
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
    /// false = Fixed days, true = Weekly goal.
    @State private var isWeekly: Bool
    /// Full day names ("Sunday" … "Saturday").
    @State private var selectedDays: Set<String>
    @State private var weeklyTarget: Int
    @State private var isRepeatable: Bool
    @State private var allOrNothing: Bool
    @State private var importance: Double
    @State private var autoApprove: Bool
    @State private var isActive: Bool
    @State private var limitDates: Bool
    @State private var startDate: Date
    @State private var endDate: Date
    @State private var saving = false
    @State private var errorMessage: String?

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
            return Set(DayPicker.allDays)
        }())
        _weeklyTarget = State(initialValue: min(7, max(1, chore?.weeklyTargetCount ?? 3)))
        _isRepeatable = State(initialValue: chore?.isRepeatable ?? false)
        _allOrNothing = State(initialValue: chore?.allOrNothing ?? false)
        _importance = State(initialValue: Double(chore?.importance ?? 0))
        _autoApprove = State(initialValue: chore?.autoApprove ?? true)
        _isActive = State(initialValue: chore?.isActive ?? true)
        _limitDates = State(initialValue: chore?.startDate != nil || chore?.endDate != nil)
        _startDate = State(initialValue: chore?.startDate?.displayDate ?? Date())
        _endDate = State(initialValue: chore?.endDate?.displayDate ?? Date())
    }

    private var isEditing: Bool { chore != nil }

    private var title: String {
        let noun = isTaskKind ? "Task" : "Routine"
        return isEditing ? "Edit \(noun)" : "New \(noun)"
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: title)

            ScrollView {
                VStack(spacing: 14) {
                    identityCard
                    kindCard
                    scheduleCard
                    whoCard
                    screenTimeCard
                    optionsCard
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
                saveTitle: isEditing ? "Save" : "Create",
                saving: saving,
                canSave: canSave,
                onCancel: { dismiss() },
                onSave: { Task { await save() } })
                .padding()
        }
        .graphiteBackground()
        #if os(macOS)
        .frame(minWidth: 460, idealWidth: 500, minHeight: 620, idealHeight: 700)
        #endif
        #if os(iOS)
        .presentationDetents([.large])
        #endif
    }

    // MARK: - Cards

    private var identityCard: some View {
        SheetCard {
            HStack(spacing: 10) {
                TextField("🧺", text: $icon)
                    .textFieldStyle(.plain)
                    .multilineTextAlignment(.center)
                    .font(.title2)
                    .frame(width: 46)
                    .padding(.vertical, 8)
                    .background(.quaternary.opacity(0.4),
                                in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    .onChange(of: icon) { _, newValue in
                        if newValue.count > 2 { icon = String(newValue.prefix(2)) }
                    }
                TextField("What's the chore?", text: $name)
                    .textFieldStyle(.plain)
                    .font(.body.weight(.medium))
                    .sheetFieldBackground()
            }
        }
    }

    private var kindCard: some View {
        SheetCard {
            Picker("Kind", selection: $isTaskKind) {
                Text("Earns").tag(true)
                Text("Expected").tag(false)
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            if isTaskKind {
                SheetField(label: "Amount") {
                    HStack(spacing: 6) {
                        Text("$")
                            .foregroundStyle(DB.gold(scheme))
                            .font(.body.weight(.semibold))
                        TextField("1.00", text: $earnText)
                            .textFieldStyle(.plain)
                            #if os(iOS)
                            .keyboardType(.decimalPad)
                            #endif
                    }
                    .sheetFieldBackground()
                }
            }

            Text(isTaskKind
                 ? "Earns money when it's done and approved."
                 : "Just expected — no money attached.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var scheduleCard: some View {
        SheetCard(title: "Schedule rule") {
            Picker("Schedule", selection: $isWeekly) {
                Text("Fixed days").tag(false)
                Text("Weekly goal").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            if isWeekly {
                Stepper("up to \(weeklyTarget)× a week", value: $weeklyTarget, in: 1...7)
                    .monospacedDigit()
                Toggle("Bonus reps allowed", isOn: $isRepeatable)
                if isTaskKind {
                    Toggle("All-or-nothing pay", isOn: $allOrNothing)
                    if allOrNothing {
                        Text("The week pays only if every rep gets done.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            } else {
                DayPicker(selected: $selectedDays)
                if selectedDays.isEmpty {
                    Text("Pick at least one day.")
                        .font(.caption)
                        .foregroundStyle(DB.help(scheme))
                }
            }
        }
    }

    @ViewBuilder
    private var whoCard: some View {
        if !children.isEmpty {
            SheetCard(title: "Who's it for") {
                Picker("For", selection: $assignedUserId) {
                    Text("Anyone").tag("")
                    ForEach(children) { child in
                        Text(child.userName).tag(child.userId)
                    }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var screenTimeCard: some View {
        SheetCard(title: "Screen time") {
            SheetField(
                label: importanceInt == 0 ? "No screen-time impact" : "Importance",
                value: importanceInt == 0 ? nil : "\(importanceInt) of 10",
                valueColor: Color.accentColor) {
                Slider(value: $importance, in: 0...10, step: 1)
            }
            if importanceInt > 0 {
                Text("Missing it costs a share of the weekly screen-time budget.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var optionsCard: some View {
        SheetCard(title: "Options") {
            Toggle(isOn: $autoApprove) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Auto-approve completions")
                    Text("No parent check needed")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }

            if isEditing {
                Toggle("Active", isOn: $isActive)
            }

            Toggle("Limit to a date range", isOn: $limitDates.animation(.snappy))
            if limitDates {
                SheetField(label: "Starts") {
                    DatePicker("", selection: $startDate, displayedComponents: .date)
                        .labelsHidden()
                }
                SheetField(label: "Ends") {
                    DatePicker("", selection: $endDate, displayedComponents: .date)
                        .labelsHidden()
                }
            }

            SheetField(label: "Notes (optional)") {
                TextField("e.g. don't forget under the bed", text: $details, axis: .vertical)
                    .textFieldStyle(.plain)
                    .lineLimit(1...3)
                    .sheetFieldBackground()
            }
        }
    }

    // MARK: - Validation / save

    private var importanceInt: Int { Int(importance) }

    /// The earn field parsed to a non-negative Decimal, or nil if invalid.
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

    /// Selected days as full names, Sunday-first. A weekly-goal chore with
    /// nothing picked falls back to every day (the server schedules within these).
    private var orderedActiveDays: [String] {
        let days = DayPicker.allDays.filter { selectedDays.contains($0) }
        return days.isEmpty ? DayPicker.allDays : days
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
            earnValue: isTaskKind ? earn : .zero,
            importance: importanceInt,
            allOrNothing: allOrNothing,
            isInverseFill: chore?.isInverseFill ?? false,
            inverseFillBaselineMinutes: chore?.inverseFillBaselineMinutes ?? 0,
            scheduleType: isWeekly ? "WeeklyFrequency" : "SpecificDays",
            activeDays: orderedActiveDays,
            weeklyTargetCount: weeklyTarget,
            isRepeatable: isWeekly ? isRepeatable : false,
            startDate: limitDates ? startDate.asDayDate() : nil,
            endDate: limitDates ? endDate.asDayDate() : nil,
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

/// Date → DayDate (the app's wire date). The server owns "today", so this is
/// only for turning the editor's date pickers back into the wire form.
fileprivate extension Date {
    func asDayDate() -> DayDate {
        let c = Calendar.current.dateComponents([.year, .month, .day], from: self)
        return DayDate(year: c.year ?? 2026, month: c.month ?? 1, day: c.day ?? 1)
    }
}
