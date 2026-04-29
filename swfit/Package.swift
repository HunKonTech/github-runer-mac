// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "GitRunnerManager",
    defaultLocalization: "en",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .executable(
            name: "GitRunnerManager",
            targets: ["GitRunnerManager"]
        ),
    ],
    targets: [
        .executableTarget(
            name: "GitRunnerManager",
            resources: [
                .process("Resources"),
            ]
        ),
        .testTarget(
            name: "GitRunnerManagerTests",
            dependencies: ["GitRunnerManager"],
            path: "tests/GitRunnerManagerTests"
        ),
    ]
)
