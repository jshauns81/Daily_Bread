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
    targets: [
        .target(name: "DailyBreadKit"),
        .testTarget(name: "DailyBreadKitTests", dependencies: ["DailyBreadKit"])
    ]
)
