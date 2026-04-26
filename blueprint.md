# github-runer-mac Blueprint

## Overview
A lightweight macOS menu bar app for managing a local GitHub Actions self-hosted runner. It shows the runner status, current activity, network condition, and launch-at-login state, and lets you start, stop, or switch back to automatic mode directly from the menu.

## Technology Stack
- **Language**: Swift 6.0
- **Platform**: macOS 14.0+
- **Package Manager**: Swift Package Manager
- **UI Framework**: SwiftUI (MenuBarExtra)
- **State Management**: @Observable, @MainActor

## Project Structure

```
github-runer-mac/
├── Package.swift
├── Sources/GitHubRunnerMenu/
│   ├── App/GitHubRunnerMenuApp.swift
│   ├── Models/RunnerModels.swift
│   ├── Services/
│   │   ├── RunnerController.swift
│   │   ├── NetworkConditionMonitor.swift
│   │   ├── RunnerResourceMonitor.swift
│   │   ├── BatteryMonitor.swift
│   │   ├── RunnerLogParser.swift
│   │   └── AppUpdateService.swift
│   ├── Settings/
│   │   ├── SettingsView.swift
│   │   └── AppPreferencesStore.swift
│   ├── Stores/RunnerMenuStore.swift
│   ├── Views/MenuPanelView.swift
│   └── Support/
│       ├── AppStrings.swift
│       ├── Shell.swift
│       └── SettingsWindowController.swift
└── tests/GitHubRunnerMenuTests/
```

## Core Functionality

### 1. Runner Process Control (RunnerController.swift)
- Starts the runner process by executing `run.sh` from the runner directory
- Stops runner processes by sending SIGINT → SIGTERM → SIGKILL signals
- Identifies runner processes by matching command line patterns:
  - `run.sh`, `run-helper.sh`, `Runner.Listener`, `Runner.Worker`
- Monitors runner status via process inspection
- Runner directory defaults to `/Users/koncsikbenedek/GitHub/actions-runner`

### 2. Activity Monitoring (RunnerLogParser.swift)
- Parses runner log files in `_diag/` directory
- Extracts current job name from log lines containing "Running job: "
- Detects states: "Listening for Jobs", job completion, "Exiting..."
- Returns `RunnerActivitySnapshot` with kind (busy/waiting/unknown) and description

### 3. Resource Monitoring (RunnerResourceMonitor.swift)
- Uses `ps -axo pid,ppid,pcpu,rss,comm` to measure CPU and memory
- Tracks `Runner.Listener` and `Runner.Worker` processes
- Returns `RunnerResourceUsage` with isRunning, isJobActive, cpuPercent, memoryMB

### 4. Network Monitoring (NetworkConditionMonitor.swift)
- Uses `NWPathMonitor` from Network framework
- Detects interface type: Ethernet, WiFi, Cellular, Other
- Classifies connection as: unmetered, expensive, offline
- Returns `NetworkConditionSnapshot` with kind and description
- Default logic: unmetered → run, expensive/offline → stop, unknown → keep current

### 5. Battery Monitoring (BatteryMonitor.swift)
- Uses IOKit (`IOPSCopyPowerSourcesInfo`, `IOPSGetPowerSourceDescription`)
- Detects: hasBattery, isOnBattery, isCharging
- Returns `BatterySnapshot`

### 6. State Management (RunnerMenuStore.swift)
- Central store using `@Observable` and `@MainActor`
- Control modes: `.automatic`, `.forceRunning`, `.forceStopped`
- Persists control mode to UserDefaults
- Reconciliation logic:
  - In automatic mode: applies network decision (run/stop/keep)
  - In force mode: directly starts/stops
  - With battery toggle: stops if running on battery
- Registers for sleep/wake notifications
- Periodic refresh every 5 seconds

### 7. App Update Service (AppUpdateService.swift)
- Fetches latest GitHub release from API
- Checks for .macos .zip or .dmg asset
- Downloads and opens installer
- Update channels: stable, preview

## UI Components

### Menu Bar (MenuPanelView.swift)
- Displays: App name, policy summary
- Status rows with colored indicators:
  - Runner: Running/Stopped (green/red)
  - Activity: Working/Waiting/Unknown (orange/gray/yellow)
  - Network: Unmetered/Expensive/Offline/Unknown (green/orange/red/gray)
  - Mode: Automatic/ForceRunning/ForceStopped (blue/green/red)
  - Launch at Login: Enabled/RequiresApproval/Disabled/NotFound (green/orange/gray/red)
- Buttons: Manual Start, Manual Stop, Automatic Mode, Refresh
- Toggle: Launch at Login
- Expandable advanced view: CPU, Memory, Job Active
- Actions: Open Settings, Quit

### Settings Window (SettingsView.swift, SettingsWindowController.swift)
- NavigationSplitView with sidebar sections:
  - **General**: Language picker, Launch at Login toggle, Stop on Battery toggle
  - **Runner**: Status, folder path, Start/Stop/Refresh buttons
  - **Updates**: Version info, Check/Install buttons, Update channel picker
  - **Network**: State, policy, override info
  - **Advanced**: Process, CPU, Memory, Job Active info
  - **About**: App name, version, license, GitHub/X/Repository links

### Localization (AppStrings.swift)
- Built-in catalogs for: en, hu, de, fr, es, it, nl, cs, da, fi, no, pl, pt-BR, pt-PT, ro, ru, sv, tr, uk, ar, he, ja, ko, zh-Hans, zh-Hant, id, vi, hi, ca, hr, el, ms, th, sk, sl, bn, gu, kn, ml, mr, or, pa, ta, te
- Dynamic language matching based on system preferences
- User can override: System default, Hungarian, English
- All UI strings defined in AppStrings enum

## System Integration

### Launch at Login (SMAppService)
- Uses `SMAppService.mainApp` for registration
- Checks status: enabled, requiresApproval, notRegistered, notFound
- Opens system login items settings via `SMAppService.openSystemSettingsLoginItems()`

### Shell Execution (Shell.swift)
- Generic process runner with stdout/stderr capture
- Returns ShellResult with status, stdout, stderr

### Logging
- Uses OSLog with subsystem "com.koncsik.githubrunnermenu"
- Categories: store, runner, network, resources, battery, updates

## Models (RunnerModels.swift)

```swift
enum RunnerControlMode: String, CaseIterable {
    case automatic
    case forceRunning
    case forceStopped
}

enum NetworkConditionKind {
    case offline, expensive, unmetered, unknown
}

struct NetworkConditionSnapshot {
    var kind: NetworkConditionKind
    var description: String
    var automaticDecision: AutomaticDecision  // run, stop, keep
}

enum RunnerActivityKind {
    case busy, waiting, unknown
}

struct RunnerActivitySnapshot {
    var kind: RunnerActivityKind
    var description: String
}

struct RunnerSnapshot {
    var isRunning: Bool
    var activity: RunnerActivitySnapshot
}

struct RunnerResourceUsage {
    var isRunning: Bool
    var isJobActive: Bool
    var cpuPercent: Double
    var memoryMB: Double
}

struct BatterySnapshot: Sendable {
    var isOnBattery: Bool
    var isCharging: Bool
    var hasBattery: Bool
    var canRun: Bool  // hasBattery && !isOnBattery
}
```

## Build Commands

```bash
# Build app bundle
./script/build_and_run.sh --bundle

# Create DMG with version
APP_VERSION=1.0.0 ./script/build_dmg.sh
```

## Key Implementation Details

1. **Runner starts**: Process runs `run.sh` with custom environment (`RUNNER_MANUALLY_TRAP_SIG=1`)
2. **Runner stops**: Multiple signals with delays (0.4s between)
3. **Network auto-decisions**: unmetered→run, expensive/offline→stop, unknown→keep
4. **Battery option**: `AppPreferencesStore.shared.stopRunnerOnBattery`
5. **Menu bar icon**: Changes based on runner state (hammer.circle.fill, play.circle.fill, pause.circle.fill)
6. **Window style**: MenuBarExtra with .window style for popover
7. **Preferences**: Stored in UserDefaults with specific keys
8. **Periodic refresh**: Every 5 seconds via Task.sleep