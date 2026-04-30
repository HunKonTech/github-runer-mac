# AGENTS.md

## English Translation Requirement

Every prompt written by the user in Hungarian (or any other language) must first be translated into a professional English prompt before processing. The translation should preserve technical terms and be clear for software engineering tasks.

## Clarification Process

After translating the user's prompt to English, examine the translated prompt carefully. Think about what is being requested and whether the requirements are clear and complete. If anything is unclear, ask the user for clarification before proceeding with implementation. Do not assume or guess unclear requirements.

## Project Overview

From README.md:

`Git runner manager` is a lightweight macOS menu bar app for managing a local GitHub Actions self-hosted runner. It shows the runner status, current activity, network condition, and launch-at-login state, and lets you start, stop, or switch back to automatic mode directly from the menu.

The app is designed for a local developer workflow where the runner should react to connectivity changes and stay easy to control without opening Terminal or the GitHub runner directory manually.

## Project Structure

A projekt két külön megoldást tartalmaz: egy natív SwiftUI macOS alkalmazást (`swfit/` mappa) és egy multiplatform Avalonia alkalmazást (`Avalonia/` mappa).

```
GitRunnerManager/
├── Assets/                          # App ikonok és egyéb asset-ek
├── README.md                       # Projekt dokumentáció
├── swfit/                          # Natív SwiftUI macOS megoldás
│   ├── Package.swift                    # Swift Package Manager config
│   ├── Sources/GitRunnerManager/
│   │   ├── App/
│   │   │   └── GitRunnerManagerApp.swift    # Main app entry point
│   │   ├── Models/
│   │   │   └── RunnerModels.swift           # Data models
│   │   ├── Services/
│   │   │   ├── AppUpdateService.swift       # App update checking
│   │   │   ├── NetworkConditionMonitor.swift # Network monitoring
│   │   │   ├── RunnerController.swift        # Runner process control
│   │   │   ├── RunnerLogParser.swift        # Log parsing
│   │   │   └── RunnerResourceMonitor.swift     # CPU/memory monitoring
│   │   ├── Settings/
│   │   │   ├── AppPreferencesStore.swift    # Preferences storage
│   │   │   └── SettingsView.swift           # Settings UI
│   │   ├── Stores/
│   │   │   └── RunnerMenuStore.swift          # Main state store
│   │   ├── Support/
│   │   │   ├── AppStrings.swift             # Localization strings
│   │   │   ├── SettingsWindowController.swift
│   │   │   └── Shell.swift                 # Shell command execution
│   │   └── Views/
│   │       └── MenuPanelView.swift            # Menu bar panel
│   └── tests/                          # Swift test files
├── Avalonia/                         # Multiplatform Avalonia megoldás
│   ├── GitRunnerManager.sln               # Visual Studio solution file
│   ├── README.md                          # Avalonia projekt dokumentáció
│   ├── scripts/                           # Build és publish scriptek
│   │   ├── build.sh                       # Build script
│   │   ├── publish-linux-x64.sh           # Linux x64 publish
│   │   ├── publish-macos-arm64.sh         # macOS ARM64 publish
│   │   └── publish-windows-x64.sh         # Windows x64 publish
│   └── src/
│       ├── GitRunnerManager.App/          # Main application project
│       ├── GitRunnerManager.Core/         # Core business logic
│       ├── GitRunnerManager.Platform/     # Platform-specific code
│       └── GitRunnerManager.Tests/        # Unit tests
├── scripts/                          # Közös build és utility scriptek
│   ├── build_and_run_avalonia.sh          # Avalonia build és futtatás
│   ├── build_and_run_swift.sh             # Swift build és futtatás
│   ├── build_dmg_swfit.sh                 # DMG készítés Swift app-hoz
│   ├── generate_icon.swift                # Ikon generáló script
│   └── post_buffer_release.py             # Release buffer script
└── tests/                          # Közös test fájlok
```

## Technology Stack

### Swift (Native macOS)
- **Language**: Swift 6.0
- **Platform**: macOS 14.0+
- **Package Manager**: Swift Package Manager
- **UI Framework**: SwiftUI (MenuBarExtra)
- **State Management**: @Observable, @MainActor
- **Architecture Pattern**: Clean Architecture with Services, Stores, Models, Views

### Avalonia (Multiplatform)
- **Language**: C#
- **Platform**: macOS, Windows, Linux
- **Package Manager**: NuGet
- **UI Framework**: Avalonia UI
- **Solution**: Visual Studio / VS Code compatible

## Key Technical Decisions

- Uses `@Observable` from Observation framework for reactive state management
- `@MainActor` for UI-bound code
- Process-based runner control via `Process` API
- Network condition monitoring via `NWPathMonitor`
- Resource monitoring via `host_processor_info` and `mach_task_info`
- Built-in localization support for English, Hungarian, and many other languages

## Build Commands

### Swift (Native macOS)
Build the app bundle locally:
```bash
./scripts/build_and_run_swift.sh --bundle
```

Create DMG:
```bash
APP_VERSION=1.0.0 ./scripts/build_dmg_swfit.sh
```

### Avalonia (Multiplatform)
Build and run:
```bash
./scripts/build_and_run_avalonia.sh
```

Build Avalonia (from Avalonia/scripts/):
```bash
./scripts/build.sh
```

Platform-specific publish (from Avalonia/scripts/):
```bash
./scripts/publish-macos-arm64.sh
./scripts/publish-linux-x64.sh
./scripts/publish-windows-x64.sh
```

## Code Style Requirements

- No comments unless explicitly requested
- Use existing patterns and conventions from the codebase
- Follow Swift best practices
- Keep code well-organized and readable
