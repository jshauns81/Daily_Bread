import SwiftUI

@main
struct BreadBoxApp: App {
    @State private var center = CommandCenter()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(center)
                .task {
                    // Status heartbeat: cheap local checks, and every finished
                    // task refreshes immediately anyway.
                    while !Task.isCancelled {
                        await center.refreshStatus()
                        try? await Task.sleep(for: .seconds(15))
                    }
                }
        }
        .defaultSize(width: 760, height: 640)
    }
}
