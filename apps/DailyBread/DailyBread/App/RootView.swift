import SwiftUI
import DailyBreadKit

struct RootView: View {
    @Environment(SessionStore.self) private var session

    var body: some View {
        Group {
            switch session.state {
            case .loading:
                ProgressView()
            case .needsServer:
                ServerSetupView()
            case .needsLogin:
                LoginView()
            case .signedIn(let user):
                MainView(user: user)
            }
        }
        #if DEBUG
        .celebrationTestLoop()
        #endif
    }
}

/// First-run: point the app at the family's server, verify it's alive.
struct ServerSetupView: View {
    @Environment(SessionStore.self) private var session
    @State private var urlText = "https://"
    @State private var checking = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 24) {
            Spacer()

            VStack(spacing: 8) {
                Text("🍞").font(.system(size: 56))
                Text("Daily Bread")
                    .font(.largeTitle.weight(.bold))
                Text("give us this day")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .italic()
                Text("Connect to your family's server")
                    .foregroundStyle(.secondary)
            }

            VStack(spacing: 12) {
                TextField("https://dailybread.example.com", text: $urlText)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.URL)
                    #if os(iOS)
                    .keyboardType(.URL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    #endif

                if let errorMessage {
                    Text(errorMessage)
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }

                Button {
                    Task { await connect() }
                } label: {
                    if checking {
                        ProgressView().frame(maxWidth: .infinity)
                    } else {
                        Text("Connect").frame(maxWidth: .infinity)
                    }
                }
                .buttonStyle(.borderedProminent)
                .disabled(checking || urlText.count < 8)
            }
            .frame(maxWidth: 420)

            Spacer()
            Spacer()
        }
        .padding()
    }

    private func connect() async {
        errorMessage = nil
        guard let url = Self.serverURL(from: urlText) else {
            errorMessage = "That doesn't look like a server address."
            return
        }
        checking = true
        defer { checking = false }
        if await session.client.checkHealth(at: url) {
            await session.setServer(url)
        } else {
            errorMessage = "Couldn't reach a Daily Bread server at \(url.absoluteString). Check the address, and that the server is running."
        }
    }

    /// Turn whatever was typed into a URL worth trying.
    ///
    /// Two traps, both of which cost an afternoon: the field starts life
    /// holding "https://", so typing a bare host silently aims TLS at a plain
    /// HTTP port; and `URL(string: "localhost:5100")` happily parses
    /// "localhost" as the *scheme*, so a `scheme != nil` check waves it through
    /// and the failure surfaces as a bare "connection refused" much later.
    ///
    /// So: only http and https count as schemes, and a bare host gets one
    /// chosen for it — http for a home server (localhost, a .local name, or a
    /// LAN IP), https for anything else, since a real domain should be TLS.
    static func serverURL(from typed: String) -> URL? {
        var text = typed.trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return nil }

        if let separator = text.range(of: "://") {
            let scheme = text[text.startIndex..<separator.lowerBound].lowercased()
            guard scheme == "http" || scheme == "https" else { return nil }
            text = scheme + text[separator.lowerBound...]
        } else {
            text = (isLocal(text) ? "http://" : "https://") + text
        }

        guard let url = URL(string: text), url.host?.isEmpty == false else { return nil }
        return url
    }

    /// A server on this machine or this house's network — the normal case for
    /// Daily Bread, and never TLS.
    private static func isLocal(_ text: String) -> Bool {
        let host = text.split(separator: "/").first.map(String.init) ?? text
        let name = host.split(separator: ":").first.map(String.init)?.lowercased() ?? host
        if name == "localhost" || name.hasSuffix(".local") { return true }
        let parts = name.split(separator: ".")
        guard parts.count == 4, parts.allSatisfy({ UInt8($0) != nil }) else { return false }
        return name.hasPrefix("10.") || name.hasPrefix("192.168.")
            || (UInt8(parts[0]) == 172 && (16...31).contains(Int(parts[1]) ?? 0))
            || name.hasPrefix("127.")
    }
}

struct LoginView: View {
    @Environment(SessionStore.self) private var session
    @State private var userName = ""
    @State private var password = ""
    @State private var busy = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(spacing: 24) {
            Spacer()

            VStack(spacing: 8) {
                Text("🍞").font(.system(size: 56))
                Text("Daily Bread")
                    .font(.largeTitle.weight(.bold))
                Text("give us this day")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .italic()
            }

            VStack(spacing: 12) {
                TextField("Username", text: $userName)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.username)
                    #if os(iOS)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    #endif

                SecureField("Password", text: $password)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.password)
                    .onSubmit { Task { await signIn() } }

                if let errorMessage {
                    Text(errorMessage)
                        .font(.footnote)
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.center)
                }

                Button {
                    Task { await signIn() }
                } label: {
                    if busy {
                        ProgressView().frame(maxWidth: .infinity)
                    } else {
                        Text("Sign In").frame(maxWidth: .infinity)
                    }
                }
                .buttonStyle(.borderedProminent)
                .disabled(busy || userName.isEmpty || password.isEmpty)
            }
            .frame(maxWidth: 420)

            Button("Change server") {
                Task { await session.forgetServer() }
            }
            .font(.footnote)
            .foregroundStyle(.secondary)

            Spacer()
            Spacer()
        }
        .padding()
    }

    private func signIn() async {
        guard !busy else { return }
        errorMessage = nil
        busy = true
        defer { busy = false }
        do {
            try await session.login(userName: userName, password: password)
        } catch {
            errorMessage = error.localizedDescription
            password = ""
        }
    }
}
