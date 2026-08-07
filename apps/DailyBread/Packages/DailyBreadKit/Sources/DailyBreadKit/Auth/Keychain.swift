import Foundation
import OSLog
import Security

private let keychainLog = Logger(subsystem: "org.dailybread.api", category: "keychain")

/// The outcome of a Keychain read, with the four cases a caller has to tell
/// apart: something is there, nothing is there, the user said no, or it broke.
public enum KeychainRead: Sendable, Equatable {
    case value(String)
    case missing
    case cancelled
    case failed(OSStatus)
}

/// Minimal Keychain wrapper for token storage. Tokens never touch
/// UserDefaults; only the (non-secret) server URL lives there.
public enum Keychain {
    private static let service = "org.dailybread.tokens"

    /// Writes report failure. They used to discard the OSStatus from both
    /// SecItemUpdate and SecItemAdd, so a store that didn't store looked
    /// exactly like one that did: sign-in succeeded, nothing persisted, and
    /// the next launch found an empty Keychain with no explanation anywhere.
    public static func set(_ value: String, forKey key: String) {
        let data = Data(value.utf8)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        let attributes: [String: Any] = [kSecValueData as String: data]
        var status = SecItemUpdate(query as CFDictionary, attributes as CFDictionary)
        if status == errSecItemNotFound {
            var addQuery = query
            addQuery[kSecValueData as String] = data
            addQuery[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            status = SecItemAdd(addQuery as CFDictionary, nil)
        }
        if status != errSecSuccess {
            keychainLog.error("✗ store \(key, privacy: .public) failed: OSStatus \(status, privacy: .public)")
        }
    }

    public static func get(_ key: String) -> String? {
        if case .value(let value) = read(key) { return value }
        return nil
    }

    /// `get` with the OSStatus kept. It used to be discarded, so "the user
    /// cancelled, stay put" and "nothing is stored, sign in again" arrived at
    /// the caller as the same `nil`.
    public static func read(_ key: String) -> KeychainRead {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        switch status {
        case errSecSuccess:
            guard let data = result as? Data,
                  let value = String(data: data, encoding: .utf8) else {
                return .failed(status)
            }
            return .value(value)
        case errSecItemNotFound: return .missing
        case errSecUserCanceled: return .cancelled
        default: return .failed(status)
        }
    }

    public static func delete(_ key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        SecItemDelete(query as CFDictionary)
    }
}
