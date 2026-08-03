// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "DailyBreadKit",
    platforms: [
        .iOS(.v17),
        .macOS(.v14)
    ],
    products: [
        .library(name: "DailyBreadKit", targets: ["DailyBreadKit"])
    ],
    dependencies: [
        // §3 YAML theming. Yams is the sanctioned parser (THEME_OWNERSHIP.md):
        // small, well-known, no strings attached.
        .package(url: "https://github.com/jpsim/Yams.git", from: "5.1.0")
    ],
    targets: [
        .target(name: "DailyBreadKit", dependencies: ["Yams"]),
        .testTarget(name: "DailyBreadKitTests", dependencies: ["DailyBreadKit"])
    ]
)
