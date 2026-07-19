import SwiftUI
import DailyBreadKit

struct RootView: View {
    @Environment(SessionStore.self) private var session

    var body: some View {
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
        guard let url = URL(string: urlText.trimmingCharacters(in: .whitespaces)),
              url.scheme != nil else {
            errorMessage = "That doesn't look like a URL."
            return
        }
        checking = true
        defer { checking = false }
        if await session.client.checkHealth(at: url) {
            await session.setServer(url)
        } else {
            errorMessage = "Couldn't find a Daily Bread server there. Check the address and that the server is running."
        }
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
