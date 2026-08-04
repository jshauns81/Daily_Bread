import Foundation

/// One server theme, YAML verbatim — the server never parses (§3.1).
public struct ThemeFileDto: Codable, Hashable, Sendable {
    public var id: String
    public var yaml: String
    public var authorUserId: String?
    public var updatedAt: LenientDate?
}

/// Who a theme on THIS device belongs to. Themes are user-bound (decided
/// 2026-08-03): the server only lists the caller's own themes, so on a shared
/// device the folder can hold files belonging to several people. Files carry
/// no owner id (meta.author is a free-text name), so this registry remembers
/// what sync learns: which user each slug belongs to, and which of MY slugs
/// the server was holding after my last sync — the difference is how a delete
/// made on one device reaches the others without tombstones.
public enum ThemeOwnership {
    private static let ownersKey = "db.themes.owners"

    static func owner(of id: String) -> String? {
        (UserDefaults.standard.dictionary(forKey: ownersKey) as? [String: String])?[id]
    }

    static func setOwner(_ userId: String?, of id: String) {
        var owners = (UserDefaults.standard.dictionary(forKey: ownersKey) as? [String: String]) ?? [:]
        owners[id] = userId
        UserDefaults.standard.set(owners, forKey: ownersKey)
    }

    /// Unowned files (hand-dropped into the folder) are visible to whoever is
    /// looking; owned files only to their owner.
    public static func isVisible(_ id: String, to userId: String?) -> Bool {
        guard let owner = owner(of: id) else { return true }
        return owner == userId
    }

    private static func syncedKey(_ userId: String) -> String { "db.themes.synced.\(userId)" }

    static func syncedIds(for userId: String) -> Set<String> {
        Set(UserDefaults.standard.stringArray(forKey: syncedKey(userId)) ?? [])
    }

    static func setSyncedIds(_ ids: Set<String>, for userId: String) {
        UserDefaults.standard.set(Array(ids).sorted(), forKey: syncedKey(userId))
    }
}

/// §3.1 — themes sync through the family's server, scoped to the signed-in
/// user: built on the iPad, visible on the Mac — for YOU. The local Themes
/// folder is the authoring surface, so on a conflict the local file wins and
/// is pushed. Server themes with no local file are written INTO the folder,
/// which is what makes them work offline and selectable at the next cold
/// launch. A theme deleted on one of my devices disappears from the server,
/// and every other device notices the id it once synced is gone and removes
/// its local file — deletion propagates without tombstones.
public enum ThemeSync {
    /// Best-effort: any network failure leaves local state untouched — the
    /// picker still works from files, exactly like offline (§3.3).
    @discardableResult
    public static func sync(_ client: APIClient, userId: String?) async -> Bool {
        guard let userId, let server = try? await client.themes() else { return false }

        let dir = ThemeLoader.themesDirectory()
        let local = ThemeLoader.available()
        let serverById = Dictionary(server.map { ($0.id, $0) },
                                    uniquingKeysWith: { a, _ in a })
        let lastSynced = ThemeOwnership.syncedIds(for: userId)
        var changedLocal = false
        var onServer = Set(server.map(\.id))

        // The server's list is authoritative about ownership of what it holds.
        for remote in server {
            ThemeOwnership.setOwner(remote.authorUserId ?? userId, of: remote.id)
        }

        for theme in local {
            guard let palette = theme.palette, let url = theme.fileURL,
                  let text = try? String(contentsOf: url, encoding: .utf8) else { continue }
            let id = palette.id
            let owner = ThemeOwnership.owner(of: id)
            guard owner == nil || owner == userId else { continue }  // someone else's copy

            if let remote = serverById[id] {
                if remote.yaml != text {
                    _ = try? await client.putTheme(id: id, yaml: text)  // local wins
                }
            } else if lastSynced.contains(id) {
                // The server had this for me and no longer does: deleted from
                // another of my devices. Follow suit.
                try? FileManager.default.removeItem(at: url)
                ThemeOwnership.setOwner(nil, of: id)
                changedLocal = true
            } else {
                // New local authoring (or a hand-dropped file): claim and push.
                do {
                    _ = try await client.putTheme(id: id, yaml: text)
                    ThemeOwnership.setOwner(userId, of: id)
                    onServer.insert(id)
                } catch let APIError.server(_, _, status) where (400..<500).contains(status) {
                    // A definitive no — the slug belongs to a sibling (a file
                    // left over from the family-bound era, or a hand-dropped
                    // copy). Record that so it leaves this user's picker and
                    // is never pushed again. Transport failures deliberately
                    // don't reach here: they retry next sync.
                    ThemeOwnership.setOwner("(someone else)", of: id)
                    changedLocal = true
                } catch { /* transient — retry on a later sync */ }
            }
        }

        // Pull mine that this device has no file for.
        let localIds = Set(local.compactMap { $0.palette?.id })
        for remote in server where !localIds.contains(remote.id) {
            let url = dir.appendingPathComponent("\(remote.id).yaml")
            if (try? remote.yaml.write(to: url, atomically: true, encoding: .utf8)) != nil {
                changedLocal = true
            }
        }

        ThemeOwnership.setSyncedIds(onServer, for: userId)

        // Only a local change alters what this device can load; push-only
        // passes keep the caches (and everything derived from them) untouched.
        if changedLocal { ThemeLoader.invalidate() }
        return true
    }
}
