import Foundation
import LocalAuthentication
import OSLog

private let authLog = Logger(subsystem: "org.dailybread.api", category: "auth")

public enum BiometryKind: String, Sendable, Equatable {
    case faceID, touchID, opticID
}

/// What this device can actually check, right now.
public enum BiometricCapability: Equatable, Sendable {
    /// Enrolled and usable.
    case biometry(BiometryKind)
    /// Five bad tries; a device unlock clears it, and the enrollment is still there.
    case biometryLockedOut(BiometryKind)
    /// No enrolled biometry, but a passcode / login password exists.
    case ownerPasscodeOnly
    /// No passcode at all — nothing to check against.
    case none
}

public extension BiometricCapability {
    var canAuthenticate: Bool {
        if case .none = self { return false }
        return true
    }

    var kind: BiometryKind? {
        switch self {
        case .biometry(let kind), .biometryLockedOut(let kind): return kind
        case .ownerPasscodeOnly, .none: return nil
        }
    }

    var displayName: String {
        switch kind {
        case .faceID: return "Face ID"
        case .touchID: return "Touch ID"
        case .opticID: return "Optic ID"
        case nil: return "your password"
        }
    }

    var symbolName: String {
        switch kind {
        case .faceID: return "faceid"
        case .touchID: return "touchid"
        case .opticID: return "opticid"
        case nil: return "lock"
        }
    }
}

/// `Error` so it can be the failure half of a `Result` — never thrown.
public enum BiometricOutcome: Error, Equatable, Sendable {
    case success
    /// User, app, or system cancel — and the fallback button, which is the
    /// same thing said differently.
    case cancelled
    /// Wrong face, wrong finger, or a rejected password.
    case failed
    case lockedOut
    /// No hardware, nothing enrolled, or no passcode set.
    case unavailable
    /// The evaluation passed but the Keychain ACL is gone (job 2 only).
    case invalidated
    /// For the log. Never shown to a user verbatim.
    case error(code: Int)
}

/// The only owner of an `LAContext` for the parent gate.
///
/// Reason strings are lowercase verb phrases — macOS renders them as
/// "Daily Bread is trying to «reason»".
@MainActor
public enum BiometricAuthenticator {

    /// Synchronous and cheap: two `canEvaluatePolicy` calls and no I/O, so it
    /// is safe to call from a view's lifecycle.
    public static func capability() -> BiometricCapability {
        let context = LAContext()
        var error: NSError?
        if context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) {
            // biometryType is only populated once canEvaluatePolicy has run —
            // reading it first reports .none on a perfectly good sensor.
            return .biometry(kind(context.biometryType))
        }
        if error?.code == LAError.biometryLockout.rawValue {
            return .biometryLockedOut(kind(context.biometryType))
        }
        // A fresh context: the one above has cached its failure.
        let fallback = LAContext()
        var fallbackError: NSError?
        if fallback.canEvaluatePolicy(.deviceOwnerAuthentication, error: &fallbackError) {
            return .ownerPasscodeOnly
        }
        return .none
    }

    /// One evaluation. Concurrent callers share the one in flight rather than
    /// stacking two system sheets — the wall auto-prompts on appear, and a
    /// user tapping Unlock in the same instant would otherwise do exactly that.
    public static func authenticate(reason: String) async -> BiometricOutcome {
        if let existing = inFlight { return await existing.value }
        let capability = capability()
        guard capability.canAuthenticate else { return .unavailable }

        let biometricsOnly = capability.kind != nil
        let task = Task<BiometricOutcome, Never> {
            await evaluate(reason: reason, biometricsOnly: biometricsOnly)
        }
        inFlight = task
        let outcome = await task.value
        inFlight = nil
        return outcome
    }

    // MARK: - Private

    private static var inFlight: Task<BiometricOutcome, Never>?

    /// Every `LAContext` is built and consumed inside one detached task: the
    /// type is not Sendable, and `evaluatePolicy` blocks whatever thread it is
    /// called on until the user answers. A context is also never reused — a
    /// second evaluation on the same one can return the first one's success
    /// without ever showing a sheet, which turns the gate into a no-op.
    private static func evaluate(reason: String, biometricsOnly: Bool) async -> BiometricOutcome {
        await Task.detached(priority: .userInitiated) {
            let context = LAContext()
            let policy: LAPolicy
            if biometricsOnly {
                policy = .deviceOwnerAuthenticationWithBiometrics
                // No in-app passcode escape, deliberately: the adversary here
                // knows the device passcode, so accepting it would make the
                // gate decorative. It is also what job 2 requires — a
                // .biometryCurrentSet item is not satisfied by a context
                // evaluated for .deviceOwnerAuthentication.
                context.localizedFallbackTitle = ""
            } else {
                policy = .deviceOwnerAuthentication
            }
            return await withCheckedContinuation { continuation in
                context.evaluatePolicy(policy, localizedReason: reason) { success, error in
                    if success {
                        continuation.resume(returning: .success)
                    } else {
                        continuation.resume(returning: map(error))
                    }
                }
            }
        }.value
    }

    nonisolated private static func map(_ error: Error?) -> BiometricOutcome {
        guard let code = (error as? LAError)?.code else {
            authLog.error("biometric evaluation failed with no LAError")
            return .error(code: 0)
        }
        switch code {
        case .userCancel, .appCancel, .systemCancel, .userFallback:
            return .cancelled
        case .authenticationFailed:
            return .failed
        case .biometryLockout:
            return .lockedOut
        case .biometryNotAvailable, .biometryNotEnrolled, .biometryNotPaired,
             .biometryDisconnected, .passcodeNotSet:
            return .unavailable
        default:
            authLog.error("biometric evaluation failed: LAError \(code.rawValue, privacy: .public)")
            return .error(code: code.rawValue)
        }
    }

    nonisolated private static func kind(_ type: LABiometryType) -> BiometryKind {
        switch type {
        case .touchID: return .touchID
        case .opticID: return .opticID
        default: return .faceID
        }
    }
}
