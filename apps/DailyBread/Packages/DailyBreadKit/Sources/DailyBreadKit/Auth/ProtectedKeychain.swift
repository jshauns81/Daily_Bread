#if os(iOS)
import Foundation
import LocalAuthentication
import OSLog
import Security

private let protectedLog = Logger(subsystem: "org.dailybread.api", category: "keychain")

/// Both tokens, together.
///
/// Not refresh-only: that forces a network round trip the instant the app
/// unlocks and dead-ends an offline launch. Not access-token-in-plaintext
/// either — a fifteen-minute full-parent bearer token sitting in an
/// AfterFirstUnlock item would make the whole feature theatre.
public struct ProtectedTokens: Codable, Sendable {
    public let access: String
    public let refresh: String

    public init(access: String, refresh: String) {
        self.access = access
        self.refresh = refresh
    }
}

/// The session sealed behind biometry (job 2, opt-in, iOS only).
///
/// macOS is excluded because the data-protection keychain there needs a
/// `keychain-access-groups` entitlement, and the sandbox entitlements are not
/// ours to change.
public enum ProtectedKeychain {
    private static let service = "org.dailybread.tokens"
    private static let account = "session.protected"

    /// Attributes only. Deliberately never asks for `kSecValueData` — that
    /// request, and only that request, raises the biometric prompt, and
    /// `bootstrap()` has to answer "is there a session here" without one.
    public static func exists() -> Bool {
        var query = baseQuery()
        query[kSecReturnAttributes as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        return SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess
    }

    /// One evaluation, one write, one read-back — all on the same context, so
    /// the parent sees exactly one prompt. The read-back is not ceremony: a
    /// write that succeeds but proves unreadable would strand the session at
    /// the next launch, with nothing on screen to explain it.
    public static func enroll(_ tokens: ProtectedTokens, reason: String) async -> BiometricOutcome {
        guard let data = try? JSONEncoder().encode(tokens) else { return .error(code: 0) }
        return await Task.detached(priority: .userInitiated) {
            let context = LAContext()
            context.localizedFallbackTitle = ""
            let evaluated = await withCheckedContinuation { continuation in
                context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics,
                                       localizedReason: reason) { success, error in
                    continuation.resume(returning: success ? BiometricOutcome.success
                                                           : mapEvaluation(error))
                }
            }
            guard evaluated == .success else { return evaluated }

            guard let access = accessControl() else { return .unavailable }
            var addQuery = baseQuery()
            addQuery[kSecValueData as String] = data
            addQuery[kSecAttrAccessControl as String] = access
            addQuery[kSecUseAuthenticationContext as String] = context
            SecItemDelete(baseQuery() as CFDictionary)
            let status = SecItemAdd(addQuery as CFDictionary, nil)
            guard status == errSecSuccess else {
                protectedLog.error("✗ protected add failed: OSStatus \(status, privacy: .public)")
                return .error(code: Int(status))
            }

            var readQuery = baseQuery()
            readQuery[kSecReturnData as String] = true
            readQuery[kSecMatchLimit as String] = kSecMatchLimitOne
            readQuery[kSecUseAuthenticationContext as String] = context
            var result: AnyObject?
            let readStatus = SecItemCopyMatching(readQuery as CFDictionary, &result)
            guard readStatus == errSecSuccess, let stored = result as? Data,
                  (try? JSONDecoder().decode(ProtectedTokens.self, from: stored)) != nil else {
                protectedLog.error("✗ protected read-back failed: OSStatus \(readStatus, privacy: .public)")
                SecItemDelete(baseQuery() as CFDictionary)
                return .error(code: Int(readStatus))
            }
            return .success
        }.value
    }

    /// The rotation path, run every fifteen minutes for the life of a session.
    /// Prompts for nothing: writing and deleting an access-controlled item does
    /// not read it. `SecItemUpdate` is not an option here for two independent
    /// reasons — it cannot attach a `kSecAttrAccessControl` to an existing item
    /// at all, and updating a protected item reads it, which would prompt.
    ///
    /// The add is attempted **before** the delete, and the delete only happens
    /// once the add has come back `errSecDuplicateItem`. Ordered the other way
    /// this is a demolition: the item is `…WhenPasscodeSetThisDeviceOnly`, so it
    /// is unwritable while the device is locked, and a rotation landing in the
    /// seconds between the screen locking and the app suspending would delete
    /// the only copy of the refresh token and then fail to write the new one.
    /// A failed probe now leaves the stored item exactly where it was.
    @discardableResult
    public static func write(_ tokens: ProtectedTokens) -> OSStatus {
        guard let data = try? JSONEncoder().encode(tokens) else { return errSecParam }
        guard let access = accessControl() else { return errSecParam }
        var addQuery = baseQuery()
        addQuery[kSecValueData as String] = data
        // With kSecAttrAccessControl supplied, kSecAttrAccessible must NOT be —
        // the add fails outright if both are present.
        addQuery[kSecAttrAccessControl as String] = access

        var status = SecItemAdd(addQuery as CFDictionary, nil)
        if status == errSecDuplicateItem {
            SecItemDelete(baseQuery() as CFDictionary)
            status = SecItemAdd(addQuery as CFDictionary, nil)
        }
        if status != errSecSuccess {
            protectedLog.error("✗ protected rotate failed: OSStatus \(status, privacy: .public)")
        }
        return status
    }

    /// The whole read lives in one detached task: `SecItemCopyMatching` on an
    /// ACL-protected item blocks its thread until the sheet is answered, and
    /// nothing but the result crosses back.
    public static func readAuthenticating(reason: String) async -> Result<ProtectedTokens, BiometricOutcome> {
        await Task.detached(priority: .userInitiated) {
            let context = LAContext()
            context.localizedFallbackTitle = ""
            let evaluated = await withCheckedContinuation { continuation in
                context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics,
                                       localizedReason: reason) { success, error in
                    continuation.resume(returning: success ? BiometricOutcome.success
                                                           : mapEvaluation(error))
                }
            }
            guard evaluated == .success else { return .failure(evaluated) }

            var query = baseQuery()
            query[kSecReturnData as String] = true
            query[kSecMatchLimit as String] = kSecMatchLimitOne
            query[kSecUseAuthenticationContext as String] = context
            var result: AnyObject?
            let status = SecItemCopyMatching(query as CFDictionary, &result)
            switch status {
            case errSecSuccess:
                guard let data = result as? Data,
                      let tokens = try? JSONDecoder().decode(ProtectedTokens.self, from: data) else {
                    return .failure(.invalidated)
                }
                return .success(tokens)
            // A match that passed and a read the Keychain still refuses is the
            // signature of .biometryCurrentSet invalidation — a new face or
            // finger, the passcode removed, or a restore onto another device.
            // It must never be confused with a cancel or with "never stored".
            case errSecItemNotFound, errSecAuthFailed, errSecInvalidItemRef:
                return .failure(.invalidated)
            case errSecUserCanceled:
                return .failure(.cancelled)
            default:
                protectedLog.error("✗ protected read: OSStatus \(status, privacy: .public)")
                return .failure(.error(code: Int(status)))
            }
        }.value
    }

    public static func remove() {
        SecItemDelete(baseQuery() as CFDictionary)
    }

    // MARK: - Private

    private static func baseQuery() -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            // A no-op on iOS; set explicitly so the flag is already in the
            // right place if the Mac path is ever unblocked.
            kSecUseDataProtectionKeychain as String: true
        ]
    }

    /// `.biometryCurrentSet` alone — no `.or, .devicePasscode` leg. The whole
    /// promise here is turning "the phone was unlocked" into "the owner was
    /// present", and in a family where the child knows the passcode a passcode
    /// leg erases exactly that. `…WhenPasscodeSetThisDeviceOnly` keeps the item
    /// out of backups, off a restored device, and evaporates it if the passcode
    /// is ever removed.
    private static func accessControl() -> SecAccessControl? {
        var error: Unmanaged<CFError>?
        let access = SecAccessControlCreateWithFlags(
            nil,
            kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly,
            .biometryCurrentSet,
            &error)
        if access == nil {
            protectedLog.error("✗ SecAccessControl: \(String(describing: error), privacy: .public)")
        }
        return access
    }

    private static func mapEvaluation(_ error: Error?) -> BiometricOutcome {
        guard let code = (error as? LAError)?.code else { return .error(code: 0) }
        switch code {
        case .userCancel, .appCancel, .systemCancel, .userFallback: return .cancelled
        case .authenticationFailed: return .failed
        case .biometryLockout: return .lockedOut
        case .biometryNotAvailable, .biometryNotEnrolled, .biometryNotPaired,
             .biometryDisconnected, .passcodeNotSet: return .unavailable
        default: return .error(code: code.rawValue)
        }
    }
}
#endif
