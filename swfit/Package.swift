// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "GitHubRunner",
    defaultLocalization: "en",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .executable(
            name: "GitHubRunner",
            targets: ["GitHubRunner"]
        ),
    ],
    targets: [
        .executableTarget(
            name: "GitHubRunner",
            resources: [
                .process("Resources"),
            ]
        ),
        .testTarget(
            name: "GitHubRunnerTests",
            dependencies: ["GitHubRunner"],
            path: "tests/GitHubRunnerTests"
        ),
    ]
)
