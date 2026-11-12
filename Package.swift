// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "FreeFlow",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(name: "FreeFlow", path: "Sources/FreeFlow")
    ]
)
