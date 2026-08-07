import SwiftUI
#if os(iOS)
import UIKit
#else
import AppKit
#endif

/// Feeds the parent gate's idle timer without ever competing for an event.
///
/// The first cut hung `.simultaneousGesture(DragGesture(minimumDistance: 0))`
/// on the shell and claimed buttons underneath would "behave exactly as they
/// did". They did not: SwiftUI still arbitrates that drag against every button
/// below it, so the Planner's New, mode-switch and Edit controls — and the task
/// and routine rows — needed two or three taps before one landed. Shaun found
/// it the first evening R4 was on his phone.
///
/// Observing one layer down cannot compete. UIKit gets a recognizer that fails
/// itself the instant it sees a touch, so it never delays, cancels or consumes
/// one; AppKit gets a passive local monitor that returns every event unchanged.
extension View {
    func reportsActivity(to gate: ParentGateActivity) -> some View {
        background(ActivityObserver(onActivity: gate.reportActivity).allowsHitTesting(false))
    }
}

/// The one thing this file needs from the gate, so the observers do not have to
/// know about `SessionStore`, biometry, or anything else in `DailyBreadKit`.
struct ParentGateActivity {
    let reportActivity: @MainActor () -> Void
}

#if os(iOS)

/// Reports the touch and immediately fails, which is what makes it invisible:
/// a recognizer in `.failed` never enters the arbitration that would otherwise
/// make a button wait its turn.
private final class PassiveTouchRecognizer: UIGestureRecognizer {
    var onTouch: (@MainActor () -> Void)?

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent) {
        MainActor.assumeIsolated { onTouch?() }
        state = .failed
    }
}

/// Hosts nothing and draws nothing; it exists to find the window, which is the
/// only place a recognizer sees every touch in the app.
private final class ProbeView: UIView {
    var onWindowChange: ((UIWindow?) -> Void)?

    override func didMoveToWindow() {
        super.didMoveToWindow()
        onWindowChange?(window)
    }
}

private struct ActivityObserver: UIViewRepresentable {
    let onActivity: @MainActor () -> Void

    func makeUIView(context: Context) -> UIView {
        let view = ProbeView()
        view.isUserInteractionEnabled = false
        view.onWindowChange = { [coordinator = context.coordinator] window in
            coordinator.attach(to: window, onActivity: onActivity)
        }
        return view
    }

    func updateUIView(_ uiView: UIView, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator: NSObject, UIGestureRecognizerDelegate {
        private weak var installed: PassiveTouchRecognizer?

        func attach(to window: UIWindow?, onActivity: @escaping @MainActor () -> Void) {
            if let installed { installed.view?.removeGestureRecognizer(installed) }
            guard let window else { return }
            let recognizer = PassiveTouchRecognizer()
            recognizer.onTouch = onActivity
            recognizer.delegate = self
            recognizer.cancelsTouchesInView = false
            recognizer.delaysTouchesBegan = false
            recognizer.delaysTouchesEnded = false
            window.addGestureRecognizer(recognizer)
            installed = recognizer
        }

        func gestureRecognizer(
            _ gestureRecognizer: UIGestureRecognizer,
            shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer
        ) -> Bool { true }
    }
}

#else

private struct ActivityObserver: NSViewRepresentable {
    let onActivity: @MainActor () -> Void

    func makeNSView(context: Context) -> NSView {
        context.coordinator.start(onActivity)
        return NSView(frame: .zero)
    }

    func updateNSView(_ nsView: NSView, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator {
        private var monitor: Any?

        func start(_ onActivity: @escaping @MainActor () -> Void) {
            guard monitor == nil else { return }
            // Local monitors run on the main thread and hand the event back
            // untouched — the app never learns this ran.
            monitor = NSEvent.addLocalMonitorForEvents(
                matching: [.leftMouseDown, .rightMouseDown, .keyDown, .scrollWheel]
            ) { event in
                MainActor.assumeIsolated { onActivity() }
                return event
            }
        }

        deinit {
            if let monitor { NSEvent.removeMonitor(monitor) }
        }
    }
}

#endif
