import SwiftUI

struct ContentView: View {
    @Environment(CommandCenter.self) private var center

    var body: some View {
        @Bindable var center = center
        VStack(spacing: 0) {
            header
            Divider()

            HStack(alignment: .top, spacing: 14) {
                group("Git", systemImage: "arrow.triangle.branch", specs: Catalog.git)
                group("Dev server", systemImage: "server.rack", specs: Catalog.server)
                group("Dev database", systemImage: "cylinder.split.1x2", specs: Catalog.database)
                group("Production", systemImage: "house.and.flag", specs: Catalog.production)
                group("Checks", systemImage: "checkmark.seal", specs: Catalog.checks)
            }
            .padding(14)

            Divider()
            logPane
        }
        .alert("Are you sure?",
               isPresented: Binding(
                   get: { center.pendingConfirmation != nil },
                   set: { if !$0 { center.pendingConfirmation = nil } })) {
            Button("Cancel", role: .cancel) { center.pendingConfirmation = nil }
            Button(center.pendingConfirmation?.title.replacingOccurrences(of: "…", with: "") ?? "Do it",
                   role: .destructive) {
                if let spec = center.pendingConfirmation { center.runConfirmed(spec) }
            }
        } message: {
            Text(center.pendingConfirmation?.confirm ?? "")
        }
    }

    // MARK: - Header: title + live status dots

    private var header: some View {
        HStack(spacing: 14) {
            Text("🍞 BreadBox")
                .font(.title3.weight(.bold))
            Spacer()
            statusDot("Dev", up: center.serverUp)
            statusDot("Postgres", up: center.postgresUp)
            statusDot("Live", up: center.prodUp)
            Text(center.gitSummary)
                .font(.caption.monospaced())
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .frame(maxWidth: 260, alignment: .trailing)
            if center.isBusy {
                ProgressView().controlSize(.small)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }

    private func statusDot(_ label: String, up: Bool) -> some View {
        HStack(spacing: 5) {
            Circle()
                .fill(up ? .green : .red)
                .frame(width: 8, height: 8)
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Button groups

    private func group(_ title: String, systemImage: String, specs: [TaskSpec]) -> some View {
        GroupBox {
            VStack(spacing: 6) {
                ForEach(specs) { spec in
                    Button {
                        center.request(spec)
                    } label: {
                        Text(spec.title)
                            .frame(maxWidth: .infinity)
                    }
                    .disabled(center.isBusy)
                }
            }
            .padding(2)
        } label: {
            Label(title, systemImage: systemImage)
                .font(.caption.weight(.semibold))
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: - Log

    private var logPane: some View {
        VStack(spacing: 0) {
            HStack {
                Text(center.runningTask.map { "Running: \($0.title)" } ?? "Log")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Clear") { center.clearLog() }
                    .controlSize(.small)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 6)

            ScrollViewReader { proxy in
                ScrollView {
                    Text(center.log)
                        .font(.callout.monospaced())
                        .textSelection(.enabled)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 14)
                        .padding(.bottom, 10)
                    Color.clear.frame(height: 1).id("bottom")
                }
                .onChange(of: center.log) {
                    proxy.scrollTo("bottom", anchor: .bottom)
                }
            }
        }
        .frame(minHeight: 220)
    }
}
