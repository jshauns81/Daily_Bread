import Foundation

/// One server theme, YAML verbatim — the server never parses (§3.1).
public struct ThemeFileDto: Codable, Hashable, Sendable {
    public var id: String
    public var yaml: String
    public var authorUserId: String?
    public var updatedAt: LenientDate?
}

/// §3.1 — themes sync through the family's server: built on the iPad, visible
/// on the Mac. The local Themes folder is the authoring surface, so on a slug
/// conflict the local file wins and is pushed; server themes with no local file
/// are written INTO the folder, which is what makes them work offline and
/// selectable at the next cold launch. Deletion propagation is deliberately
/// not here yet — removing a file locally just stops pushing it.
public enum ThemeSync {
    /// Best-effort: any network failure leaves local state untouched — the
    /// picker still works from files, exactly like offline (§3.3).
    @discardableResult
    public static func sync(_ client: APIClient) async -> Bool {
        guard let server = try? await client.themes() else { return false }

        let dir = ThemeLoader.themesDirectory()
        let local = ThemeLoader.available()
        let serverById = Dictionary(server.map { ($0.id, $0) },
                                    uniquingKeysWith: { a, _ in a })

        // Push: every VALID local theme whose text differs from the server's.
        // Invalid files never leave the device — the server shouldn't host a
        // file the picker won't select.
        for theme in local {
            guard let palette = theme.palette, let url = theme.fileURL,
                  let text = try? String(contentsOf: url, encoding: .utf8) else { continue }
            if serverById[palette.id]?.yaml != text {
                _ = try? await client.putTheme(id: palette.id, yaml: text)
            }
        }

        // Pull: server themes this device has no file for.
        let localIds = Set(local.compactMap { $0.palette?.id })
        for remote in server where !localIds.contains(remote.id) {
            let url = dir.appendingPathComponent("\(remote.id).yaml")
            try? remote.yaml.write(to: url, atomically: true, encoding: .utf8)
        }

        ThemeLoader.invalidate()
        return true
    }
}
