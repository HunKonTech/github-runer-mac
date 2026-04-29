# github-runer-mac Blueprint

## Overview

A lightweight menu bar / tray application for managing a local GitHub Actions self-hosted runner. It shows the runner status, current activity, network condition, and launch-at-login state, and lets you start, stop, or switch back to automatic mode directly from the menu.

The repository contains **two separate implementations** of the same product:

| Solution | Stack | Platform |
|----------|-------|----------|
| **SwiftUI** (native) | Swift 6.0, SwiftUI, Apple frameworks | macOS 14.0+ only |
| **Avalonia** (cross-platform) | C# .NET 10, Avalonia UI | macOS, Windows, Linux |

Both implementations share the same domain model and reconciliation logic but differ in architecture, UI framework, and platform integration.

---

# Part I — SwiftUI Blueprint (Native macOS)

## Technology Stack

- **Language**: Swift 6.0
- **Platform**: macOS 14.0+
- **Package Manager**: Swift Package Manager
- **UI Framework**: SwiftUI (MenuBarExtra)
- **State Management**: `@Observable`, `@MainActor`
- **External Dependencies**: None (pure Apple frameworks)

## Project Structure

```
swfit/
├── Package.swift
├── Assets/                          # App icons (.icns, .iconset)
├── Sources/GitHubRunner/
│   ├── App/
│   │   └── GitHubRunnerMenuApp.swift       # @main entry point
│   ├── Models/
│   │   └── RunnerModels.swift              # Domain models & enums
│   ├── Services/
│   │   ├── AppUpdateService.swift           # GitHub release checking & DMG install
│   │   ├── BatteryMonitor.swift             # IOKit battery state monitoring
│   │   ├── NetworkConditionMonitor.swift    # NWPathMonitor for network state
│   │   ├── RunnerController.swift           # Process lifecycle management
│   │   ├── RunnerLogParser.swift            # Log file parsing for activity
│   │   └── RunnerResourceMonitor.swift      # CPU/memory measurement via ps
│   ├── Settings/
│   │   ├── AppPreferencesStore.swift        # UserDefaults-backed preferences
│   │   └── SettingsView.swift               # Multi-pane Settings window UI
│   ├── Stores/
│   │   └── RunnerMenuStore.swift            # Central @Observable state store
│   ├── Support/
│   │   ├── AppStrings.swift                 # Custom localization system (50+ langs)
│   │   ├── SettingsWindowController.swift   # NSWindow management for Settings
│   │   └── Shell.swift                      # Process execution wrapper
│   ├── Resources/
│   │   ├── en.lproj/Localizable.strings     # English .strings
│   │   └── hu.lproj/Localizable.strings     # Hungarian .strings
│   └── Views/
│       └── MenuPanelView.swift              # MenuBarExtra panel UI
└── tests/
    └── GitHubRunnerTests/
        └── AppPreferencesStoreTests.swift   # XCTest for preferences
```

## Architecture

```
MenuBarExtra ──> MenuPanelView ──> RunnerMenuStore (@Observable, @MainActor)
                                        │
                                        ├── RunnerController (Process API)
                                        ├── NetworkConditionMonitor (NWPathMonitor)
                                        ├── RunnerResourceMonitor (ps polling)
                                        ├── BatteryMonitor (IOKit polling)
                                        ├── AppPreferencesStore (UserDefaults)
                                        └── AppUpdateService (GitHub API)
```

### Data Flow

1. `GitHubRunnerMenuApp` creates `RunnerMenuStore` with all service dependencies
2. `RunnerMenuStore.startMonitoring()` wires up callbacks from all monitors
3. Monitors push state changes → store reconciles → `@Observable` triggers SwiftUI re-render
4. Periodic 5-second refresh ensures stale state is corrected

## Core Functionality

### 1. Runner Process Control (`RunnerController.swift`)

| Aspect | Detail |
|--------|--------|
| Start | Executes `run.sh` via `Process` with `RUNNER_MANUALLY_TRAP_SIG=1` |
| Stop | SIGINT → 0.4s → SIGTERM → 0.4s → SIGKILL |
| Detection | Parses `ps -axo pid=,command=` output |
| Matching patterns | `run.sh`, `run-helper.sh`, `Runner.Listener`, `Runner.Worker` |
| Default directory | `/Users/koncsikbenedek/GitHub/actions-runner` |

### 2. Activity Monitoring (`RunnerLogParser.swift`)

| Log Pattern | Detected State |
|-------------|---------------|
| `"Running job: "` | `.busy` (with job name) |
| `"Listening for Jobs"` | `.waiting` |
| `" completed with result:"` | `.waiting` |
| `"Exiting..."` | `.unknown` (stopping) |

Reads the most recently modified `Runner_*.log` file from `_diag/` directory.

### 3. Resource Monitoring (`RunnerResourceMonitor.swift`)

- Polls every 2 seconds via detached `Task`
- Command: `ps -axo pid,ppid,pcpu,rss,comm`
- Aggregates CPU% and RSS memory for `Runner.Listener` and `Runner.Worker`
- `isJobActive` = `Runner.Worker` process present

### 4. Network Monitoring (`NetworkConditionMonitor.swift`)

- Uses `NWPathMonitor` from Network framework
- Runs on dedicated `DispatchQueue`
- Maps `NWPath.status` and `NWPath.isConstrained` to:
  - `.offline` → path unavailable
  - `.expensive` → cellular/personal hotspot
  - `.unmetered` → WiFi/Ethernet
- Detects interface type: Ethernet, WiFi, Cellular, Other

### 5. Battery Monitoring (`BatteryMonitor.swift`)

- Uses IOKit: `IOPSCopyPowerSourcesInfo`, `IOPSGetPowerSourceDescription`
- Polls every 10 seconds via `DispatchSource` timer
- Exposes: `hasBattery`, `isOnBattery`, `isCharging`
- Computed: `canRun = hasBattery && !isOnBattery`

### 6. State Management (`RunnerMenuStore.swift`)

**Central `@Observable` `@MainActor` store.**

#### Properties

| Property | Type |
|----------|------|
| `controlMode` | `RunnerControlMode` (persisted) |
| `launchAtLoginStatus` | `SMAppService.Status` |
| `networkSnapshot` | `NetworkConditionSnapshot` |
| `runnerSnapshot` | `RunnerSnapshot` |
| `runnerResourceUsage` | `RunnerResourceUsage` |
| `batterySnapshot` | `BatterySnapshot` |
| `lastErrorMessage` | `String?` |

#### Reconciliation Logic

```
reconcileState():
  1. If stopRunnerOnBattery && isOnBattery → stop runner
  2. Switch controlMode:
     .automatic:
       switch networkSnapshot.automaticDecision:
         .run  → start runner
         .stop → stop runner
         .keep → no-op
     .forceRunning → start runner
     .forceStopped → stop runner
```

#### Trigger Points

- App launch
- Network condition change (event-driven)
- Battery state change (event-driven)
- Wake from sleep (NSNotification)
- Control mode change (user action)
- Periodic timer (every 5 seconds)

### 7. App Update Service (`AppUpdateService.swift`)

| Aspect | Detail |
|--------|--------|
| API | `GET /repos/HunKonTech/github-runer-mac/releases/latest` |
| Asset selection | `.macos.zip` or `.dmg` (arm64/x86_64) |
| Channels | `.stable` (latest), `.preview` (all releases) |
| States | `idle`, `checking`, `upToDate`, `updateAvailable`, `downloading`, `installing`, `failed` |
| Version comparison | Semantic version parsing |

### 8. Preferences (`AppPreferencesStore.swift`)

| Preference | Default | Key |
|------------|---------|-----|
| `language` | `.system` | `AppLanguagePreference` |
| `automaticUpdateCheckEnabled` | `false` | `AutomaticUpdateCheckEnabled` |
| `updateChannel` | `.stable` | `UpdateChannel` |
| `stopRunnerOnBattery` | `false` | `StopRunnerOnBattery` |

Persisted via `UserDefaults.standard`.

## Models (`RunnerModels.swift`)

```swift
enum RunnerControlMode: String, CaseIterable {
    case automatic, forceRunning, forceStopped
}

enum NetworkConditionKind { case offline, expensive, unmetered, unknown }

struct NetworkConditionSnapshot {
    var kind: NetworkConditionKind
    var description: String
    var automaticDecision: AutomaticDecision  // .run, .stop, .keep
}

enum RunnerActivityKind { case busy, waiting, unknown }

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

## UI Components

### Menu Bar Panel (`MenuPanelView.swift`)

```
MenuBarExtra (.window style)
└── MenuPanelView (VStack, 320px width)
    ├── App name + policy summary
    ├── StatusRow — Runner (green/red)
    ├── StatusRow — Activity (orange/gray/yellow)
    ├── StatusRow — Network (green/orange/red/gray)
    ├── StatusRow — Mode (blue/green/red)
    ├── StatusRow — Launch at Login (green/orange/gray/red)
    ├── DisclosureGroup — CPU, Memory, Job Active
    ├── Error message (if present)
    ├── Buttons: Start / Stop / Automatic / Refresh
    ├── Toggle: Launch at Login
    ├── Button: Settings...
    └── Button: Quit
```

### Settings Window (`SettingsView.swift`)

```
NavigationSplitView (700×500)
├── Sidebar — 6 sections
│   ├── General (gearshape)
│   ├── Runner (play.rectangle)
│   ├── Updates (arrow.down.circle)
│   ├── Network (network)
│   ├── Advanced (wrench.and.screwdriver)
│   └── About (info.circle)
└── Detail pane — per-section content
```

| Section | Controls |
|---------|----------|
| General | Language picker, Launch at Login toggle, Stop on Battery toggle |
| Runner | Status, folder path, Start/Stop/Refresh |
| Updates | Version, auto-check toggle, channel picker, Check/Install |
| Network | Current state, policy explanation |
| Advanced | CPU%, Memory, Job active |
| About | App name, version, MIT license, GitHub/X links |

### Localization (`AppStrings.swift`)

- Custom in-Swift catalog system (1311 lines, ~115 keys)
- Fallback chain: language-specific catalog → English → key rawValue
- User language override: System, Hungarian, English
- Full catalogs: English, Hungarian
- Partial catalogs: de, fr, es, it, nl, cs, da, fi, no, pl, pt-BR, pt-PT, ro, ru, sv, tr, uk, ar, he, ja, ko, zh-Hans, zh-Hant, id, vi, hi, ca, hr, el, ms, th, sk, sl, bn, gu, kn, ml, mr, or, pa, ta, te, ur

## System Integration

| Feature | API |
|---------|-----|
| Launch at Login | `SMAppService.mainApp` (register/status/unregister) |
| Shell execution | `Process` with `Pipe` for stdout/stderr |
| Battery | IOKit (`IOPSCopyPowerSourcesInfo`) |
| Network | `NWPathMonitor` (Network framework) |
| Resources | `ps` command parsing |
| Logging | `os.Logger` with subsystem `com.koncsik.githubrunnermenu` |
| Sleep/Wake | `NSWorkspace.didWakeNotification` |

## Build Commands

```bash
# Build and run
./scripts/build_and_run_swift.sh

# Build app bundle
./scripts/build_and_run_swift.sh --bundle

# Create DMG
APP_VERSION=1.0.0 ./scripts/build_dmg_swfit.sh
```

---

# Part II — Avalonia Blueprint (Cross-Platform)

## Technology Stack

- **Language**: C# (.NET 10.0)
- **UI Framework**: Avalonia 12.0.1
- **MVVM**: CommunityToolkit.Mvvm 8.3.2
- **DI**: Microsoft.Extensions.DependencyInjection 9.0.0
- **Runtime Identifiers**: win-x64, osx-arm64, linux-x64
- **Test Framework**: xUnit 2.9.2

## Project Structure

```
Avalonia/
├── GitHubRunnerTray.sln
├── README.md
├── scripts/
│   ├── build.sh
│   ├── publish-linux-x64.sh
│   ├── publish-macos-arm64.sh
│   └── publish-windows-x64.sh
├── publish/                          # Build outputs
│   ├── linux-x64/
│   ├── macos/
│   ├── macos-arm64/
│   └── win-x64/
└── src/
    ├── GitHubRunnerTray.Core/        # Domain layer (interfaces, models, services)
    │   ├── GitHubRunnerTray.Core.csproj
    │   ├── Interfaces/Interfaces.cs
    │   ├── Localization/LocalizationKeys.cs
    │   ├── Models/RunnerModels.cs
    │   └── Services/
    │       ├── LocalizationService.cs
    │       ├── RunnerLogParser.cs
    │       └── RunnerTrayStore.cs
    │
    ├── GitHubRunnerTray.Platform/    # Platform-specific implementations
    │   ├── GitHubRunnerTray.Platform.csproj
    │   ├── Properties/AssemblyInfo.cs
    │   └── Services/
    │       ├── AppUpdateService.cs
    │       ├── BatteryMonitor.cs
    │       ├── LaunchAtLoginService.cs
    │       ├── NetworkConditionMonitor.cs
    │       ├── PreferencesStore.cs
    │       ├── ResourceMonitor.cs
    │       └── RunnerController.cs
    │
    ├── GitHubRunnerTray.App/         # Main UI application
    │   ├── GitHubRunnerTray.App.csproj
    │   ├── Program.cs
    │   ├── App.axaml / App.axaml.cs
    │   ├── MainWindow.cs
    │   ├── TrayMenuWindow.cs
    │   ├── SettingsWindow.cs
    │   ├── AboutWindow.cs
    │   ├── BatteryMonitorFactory.cs
    │   ├── app.manifest
    │   └── Assets/
    │       ├── AppIcon.appiconset/AppIcon.png
    │       ├── Icon.ico
    │       ├── Icon.png
    │       ├── TrayBusy.png
    │       ├── TrayPaused.png
    │       └── TrayWaiting.png
    │
    └── GitHubRunnerTray.Tests/       # Unit tests (xUnit)
        ├── GitHubRunnerTray.Tests.csproj
        ├── PreferencesStoreTests.cs
        ├── ResourceMonitorTests.cs
        └── RunnerLogParserTests.cs
```

## Architecture

```
                    ┌──────────────────────────────────┐
                    │   GitHubRunnerTray.App            │
                    │   (Windows, Tray, DI setup)       │
                    └──────────────┬───────────────────┘
                                   │ references
                    ┌──────────────▼───────────────────┐
                    │   GitHubRunnerTray.Platform       │
                    │   (OS-specific implementations)   │
                    └──────────────┬───────────────────┘
                                   │ references
                    ┌──────────────▼───────────────────┐
                    │   GitHubRunnerTray.Core           │
                    │   (Interfaces, Models, Domain)    │
                    └──────────────────────────────────┘
```

### Dependency Graph

```
GitHubRunnerTray.App
├── GitHubRunnerTray.Core
│   ├── CommunityToolkit.Mvvm
│   ├── Microsoft.Extensions.DependencyInjection.Abstractions
│   └── Microsoft.Extensions.Hosting.Abstractions
├── GitHubRunnerTray.Platform
│   ├── GitHubRunnerTray.Core
│   ├── Microsoft.Extensions.DependencyInjection.Abstractions
│   └── System.Management (Windows WMI)
└── Direct packages
    ├── Avalonia 12.0.1
    ├── Avalonia.Desktop 12.0.1
    ├── Avalonia.Themes.Simple 12.0.1
    ├── CommunityToolkit.Mvvm 8.3.2
    ├── Microsoft.Extensions.DependencyInjection 9.0.0
    ├── Microsoft.Extensions.Hosting 9.0.0
    └── Microsoft.Extensions.Http 9.0.0
```

### Key Patterns

| Pattern | Usage |
|---------|-------|
| **Clean Architecture** | Core → Platform → App layering |
| **Factory Pattern** | All platform services created via factories |
| **Observer Pattern** | Events (`OnChange`, `PropertyChanged`) |
| **MVVM** | `CommunityToolkit.Mvvm` `[ObservableProperty]` |
| **Tray-only App** | No main window, `ShutdownMode.OnExplicitShutdown` |
| **Programmatic UI** | All windows built in C# code (no .axaml markup) |

## Core Functionality

### 1. Runner Process Control (`RunnerController.cs`)

| Aspect | Detail |
|--------|--------|
| Start | Executes `run.sh` / `run.cmd` / `run.bat` via `Process` |
| Stop | Kills entire process tree (parent + children) |
| Detection | Matches process names: `Runner.Listener`, `Runner.Worker`, `run`, `run-helper` |
| Environment | `RUNNER_MANUALLY_TRAP_SIG=1` |
| Cross-platform | Detects Windows vs Unix executable paths |

### 2. Activity Monitoring (`RunnerLogParser.cs`)

| Log Pattern | Detected State |
|-------------|---------------|
| `"Running job: "` | `Busy` (with job name) |
| `"Listening for Jobs"` | `Waiting` |
| `" completed with result:"` | `Waiting` |
| `"Exiting..."` | `Unknown` |

Reads `Runner_*.log` from `_diag/` directory.

### 3. Resource Monitoring (`ResourceMonitor.cs`)

| Platform | Method |
|----------|--------|
| Unix | `/bin/ps -axo pid=,ppid=,pcpu=,rss=,comm=,args=` |
| Windows | `wmic process get ProcessId,ParentProcessId,Name,CommandLine,WorkingSetSize` |

Uses `ProcessTreeResourceAggregator` to:
- Build parent-child process tree
- Find runner root by matching directory in command line
- Aggregate CPU% and memory across all related processes
- Detect active jobs via `Runner.Worker` presence

### 4. Network Monitoring (`NetworkConditionMonitor.cs`)

| Platform | Method |
|----------|--------|
| macOS | `/sbin/route -n get default`, `/usr/sbin/scutil --nwi`, `/usr/sbin/networksetup -listallhardwareports` |
| Generic | `NetworkInterface.GetAllNetworkInterfaces()` |

Detects:
- Expensive/metered connections
- Interface type (Ethernet, WiFi, etc.)
- Offline state

### 5. Battery Monitoring (`BatteryMonitor.cs`)

| Platform | Method |
|----------|--------|
| Windows | `Win32_Battery` WMI query |
| macOS | `pmset -g battstatus` command |
| Linux | `/sys/class/power_supply` sysfs |

Exposes: `HasBattery`, `IsOnBattery`, `IsCharging`, `CanRun`.

### 6. State Management (`RunnerTrayStore.cs`)

**Central state store using `CommunityToolkit.Mvvm` `[ObservableProperty]`.**

#### Properties

| Property | Type |
|----------|------|
| `ControlMode` | `RunnerControlMode` |
| `RunnerSnapshot` | `RunnerSnapshot` |
| `NetworkSnapshot` | `NetworkConditionSnapshot` |
| `BatterySnapshot` | `BatterySnapshot` |
| `ResourceUsage` | `RunnerResourceUsage` |
| `LaunchAtLoginEnabled` | `bool` |
| `LastErrorMessage` | `string?` |
| `LastRefreshTime` | `DateTimeOffset` |

#### Reconciliation Logic (`ReconcileStateAsync`)

```
1. Battery check: if StopRunnerOnBattery && IsOnBattery → stop
2. Switch ControlMode:
   Automatic:
     switch NetworkSnapshot.AutomaticDecision:
       Run    → start runner
       Stop   → stop runner (unless metered pausing is off)
       Keep   → no-op
   ForceRunning → start runner
   ForceStopped → stop runner
```

- 5-second refresh timer
- Network condition events trigger immediate reconciliation
- Factory pattern for all platform services

### 7. Preferences (`PreferencesStore.cs`)

| Preference | Default |
|------------|---------|
| `Language` | `System` |
| `RunnerDirectory` | Platform default |
| `ControlMode` | `Automatic` |
| `AutomaticUpdateCheckEnabled` | `false` |
| `UpdateChannel` | `Stable` |
| `StopRunnerOnBattery` | `false` |
| `StopRunnerOnMeteredNetwork` | `false` |

Persisted as JSON to platform-specific location:

| Platform | Path |
|----------|------|
| macOS | `~/Library/Application Support/GitHubRunnerTray/settings.json` |
| Windows | `%APPDATA%/GitHubRunnerTray/settings.json` |
| Linux | `~/.config/GitHubRunnerTray/settings.json` |

### 8. App Update Service (`AppUpdateService.cs`)

| Aspect | Detail |
|--------|--------|
| API | `GET /repos/HunKonTech/github-runer-mac/releases` |
| Channels | `Stable` (latest), `Preview` (all) |
| Asset matching | Platform-specific (win-x64, osx-arm64, linux-x64) |
| Download | HTTP download + open installer |

### 9. Launch at Login (`LaunchAtLoginService.cs`)

| Platform | Method |
|----------|--------|
| macOS | `~/Library/LaunchAgents/com.koncsikbenedek.github-runner-tray.plist` |
| Windows | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

Status enum: `Enabled`, `RequiresApproval`, `Disabled`, `Unavailable`, `Unknown`.

## Models (`RunnerModels.cs`)

```csharp
enum RunnerControlMode { Automatic, ForceRunning, ForceStopped }
enum NetworkConditionKind { Offline, Expensive, Unmetered, Unknown }
enum NetworkDecision { Run, Stop, Keep }
enum RunnerActivityKind { Busy, Waiting, Unknown }
enum AppLanguage { System, Hungarian, English }
enum UpdateChannel { Stable, Preview }
enum LaunchAtLoginStatus { Enabled, RequiresApproval, Disabled, Unavailable, Unknown }

class NetworkConditionSnapshot { Kind, Description, AutomaticDecision }
class RunnerActivitySnapshot { Kind, Description }
class RunnerSnapshot { IsRunning, Activity }
class RunnerResourceSnapshot { CpuPercent, MemoryMB, ProcessCount }
class RunnerResourceUsage { IsRunning, IsJobActive, CpuPercent, MemoryMB }
class ProcessResourceInfo { Pid, Ppid, CpuPercent, MemoryKB, Name, Args }
class BatterySnapshot { IsOnBattery, IsCharging, HasBattery, CanRun }
class AppUpdateInfo { Version, ReleasePageUrl, DownloadUrl, PublishedAt }
class PreferenceDefaults { ... }
```

## Interfaces (`Interfaces.cs`)

| Interface | Purpose |
|-----------|---------|
| `IRunnerController` | Start/stop runner, get snapshot |
| `IRunnerControllerFactory` | Creates `IRunnerController` |
| `IRunnerLogParser` | Parse runner logs |
| `IResourceMonitor` | Get CPU/memory, stop monitoring |
| `IResourceMonitorFactory` | Creates `IResourceMonitor` |
| `INetworkConditionMonitor` | Network state change events |
| `IBatteryMonitor` | Battery state change events |
| `IBatteryMonitorFactory` | Creates `IBatteryMonitor` |
| `IPreferencesStore` | Read/write preferences |
| `IPreferencesStoreFactory` | Creates `IPreferencesStore` |
| `IAppUpdateService` | Check updates, download installer |
| `ILaunchAtLoginService` | Get/set launch-at-login |
| `ILaunchAtLoginServiceFactory` | Creates `ILaunchAtLoginService` |
| `IClock` | Time abstraction |
| `IFileSystem` | File system abstraction |

## UI Components

### Tray Menu Window (`TrayMenuWindow.cs` — 568 lines)

```
TrayMenuWindow (390px wide, dark theme #171717)
├── App name + policy summary
├── StatusRow — Runner (green/red)
├── StatusRow — Activity (orange/gray)
├── StatusRow — Network (green/orange/red)
├── StatusRow — Mode (blue/green/red)
├── Advanced section — CPU, Memory, Job Active
├── Error message (if present)
├── Buttons: Start / Stop / Automatic / Refresh
├── Toggle: Launch at Login
├── Button: Settings
└── Button: Quit
```

- Auto-hides on deactivation
- Uses CoreGraphics P/Invoke for pointer location on macOS
- Tray icon changes: `TrayBusy.png`, `TrayWaiting.png`, `TrayPaused.png`

### Settings Window (`SettingsWindow.cs` — 751 lines)

```
SettingsWindow (with sidebar navigation)
├── General — Language picker, Launch at Login, Stop on Battery
├── Runner — Status, folder path (with picker), Start/Stop/Refresh
├── Updates — Version, auto-check toggle, channel picker, Check/Install
├── Network — Current state, policy, override info
├── Advanced — Process info, CPU%, Memory, Job active
└── About — App name, version, license, GitHub/X/Repository links
```

### About Window (`AboutWindow.cs`)

Simple dialog with version and author info.

## Localization (`LocalizationService.cs`)

- 134 localization keys (`LocalizationKeys.cs`)
- Two full catalogs: English, Hungarian
- System language detection via `CultureInfo.CurrentUICulture`
- Format string support (`{0}` placeholders)
- User can override: System, Hungarian, English

## System Integration

| Feature | Platform | Implementation |
|---------|----------|----------------|
| Launch at Login | macOS | LaunchAgents plist |
| Launch at Login | Windows | Registry Run key |
| Battery | Windows | WMI `Win32_Battery` |
| Battery | macOS | `pmset -g batt` |
| Battery | Linux | sysfs `/sys/class/power_supply` |
| Network | macOS | `scutil`, `route`, `networksetup` |
| Network | Generic | `NetworkInterface.GetAllNetworkInterfaces()` |
| Resources | Unix | `ps` command |
| Resources | Windows | `wmic` command |
| Preferences | All | JSON file per-platform |
| Tray Icon | All | Avalonia `TrayIcon` API |
| Pointer Location | macOS | CoreGraphics P/Invoke |

## Tests (`GitHubRunnerTray.Tests/`)

| Test File | Coverage |
|-----------|----------|
| `PreferencesStoreTests.cs` | Preference persistence, defaults, round-trip |
| `ResourceMonitorTests.cs` | Process parsing, tree aggregation |
| `RunnerLogParserTests.cs` | Log pattern matching, activity detection |

Test framework: xUnit 2.9.2 with coverlet for code coverage.

## Build Commands

```bash
# Build all projects
cd Avalonia && ./scripts/build.sh

# Publish for specific platform
./scripts/publish-macos-arm64.sh
./scripts/publish-windows-x64.sh
./scripts/publish-linux-x64.sh
```

## Key Implementation Details

1. **Programmatic UI**: All windows built in C# code, no `.axaml` markup for windows
2. **Tray-only app**: No main window, `desktop.MainWindow = null`
3. **macOS dock hiding**: `ShowInDock = false` via `MacOSPlatformOptions`
4. **Dark theme**: `#171717` background for tray menu
5. **Factory pattern**: Every platform service created via factory interface
6. **Process tree killing**: Stops entire runner process tree, not just parent
7. **JSON preferences**: Platform-specific app data directories
8. **Cross-platform battery**: Three different implementations (WMI, pmset, sysfs)

---

# Comparison: SwiftUI vs Avalonia

| Aspect | SwiftUI | Avalonia |
|--------|---------|----------|
| **Platform** | macOS only | macOS, Windows, Linux |
| **Language** | Swift 6.0 | C# (.NET 10) |
| **UI Framework** | SwiftUI (MenuBarExtra) | Avalonia (programmatic) |
| **State Management** | `@Observable` + `@MainActor` | `CommunityToolkit.Mvvm` |
| **Dependency Injection** | Manual wiring | `Microsoft.Extensions.DependencyInjection` |
| **Localization** | Custom catalog (50+ langs) | Bilingual (en/hu) |
| **Preferences** | `UserDefaults` | JSON file |
| **Launch at Login** | `SMAppService` | LaunchAgents plist / Registry |
| **External Dependencies** | None | Avalonia, CommunityToolkit, MS.Extensions |
| **Build System** | SPM | dotnet CLI |
| **Architecture** | Services + Store | Clean Architecture (Core/Platform/App) |
| **UI Definition** | Declarative SwiftUI | Programmatic C# |
| **Test Framework** | XCTest | xUnit |
