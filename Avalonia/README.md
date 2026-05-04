# GitRunnerManager (Avalonia)

## Magyar

A **GitRunnerManager Avalonia** egy Windows, macOS és Linux alatt futó asztali alkalmazás GitHub Actions self-hosted runnerek kezelésére. A Swift macOS app multiplatform változata C# / .NET 9 és Avalonia UI alapon.

### Funkciók

- Rendszertálca vagy menüsor ikon runner állapottal
- Több runner profil kezelése
- Indítás, leállítás, újraindítás és automatikus mód
- Összes runner indítása, leállítása és frissítése
- Runner hozzáadása varázslóval
- Meglévő runner mappa importálása
- Repository és organization runner konfiguráció
- Runner engedélyezés, automatikus indítás és automatikus mód profilonként
- Akkumulátor és forgalomkorlátos hálózat alapján történő szüneteltetés
- CPU-, memória-, folyamat- és aktív job figyelés
- Runner log nézet, log mappa megnyitása
- Runner frissítés egyenként vagy tömegesen
- GitHub fiókok kezelése személyes és szervezeti hozzáféréssel
- GitHub OAuth Device Flow és GitHub CLI token fallback
- GitHub Actions irányítópult
- Workflow futások, jobok, lépések és státuszok megjelenítése
- Runner group és engedélyezett repositoryk megjelenítése, ha a jogosultság elérhető
- Helyi runner aktivitás és GitHub job korreláció
- Repository szűrő az Actions irányítópulton
- Diagnosztikai kontextus másolása LLM-hez
- Markdown és JSON export
- Alkalmazásfrissítés GitHub Releases alapján
- Stabil és preview frissítési csatorna
- Automatikus frissítéskeresés
- Magyar és angol lokalizáció
- Fejlesztői diagnosztikai logok

### Támogatott platformok

- **Windows 10/11**: rendszertálca ikon
- **macOS 11+**: menüsor ikon
- **Linux**: AppIndicator alapú TrayIcon

Linuxon a tálca támogatása a desktop környezettől függ.

### Runner mappák

Alapértelmezett útvonalak:

- macOS: `/Users/<felhasználó>/actions-runner`
- Linux: `/home/<felhasználó>/actions-runner`
- Windows: `C:\Users\<felhasználó>\GitHub\actions-runner`

A Runnerek beállítási oldalon több runner is hozzáadható, importálható vagy eltávolítható.

### GitHub integráció

Az Actions irányítópult GitHub hozzáféréssel tölti be a runner metaadatokat, workflow futásokat és job részleteket. Repository runnerhez repository Actions olvasási jogosultság kell. Organization runner group és hozzáférési adatokhoz organization admin jogosultság szükséges lehet.

OAuth Device Flow használatához GitHub OAuth App Client ID szükséges. Ha nincs beállítva, az app megpróbálhat GitHub CLI tokent használni.

### Build

```bash
dotnet build
```

Release build:

```bash
./scripts/build.sh
```

Publish:

```bash
APP_VERSION=1.0.0 ./scripts/publish-macos-arm64.sh
APP_VERSION=1.0.0 ./scripts/publish-windows-x64.sh
APP_VERSION=1.0.0 ./scripts/publish-windows-arm64.sh
APP_VERSION=1.0.0 ./scripts/publish-linux-x64.sh
APP_VERSION=1.0.0 ./scripts/publish-linux-arm64.sh
```

Windows MSIX:

```powershell
.\scripts\publish-windows-msix.ps1
```

A publikált appok a `publish/` mappába kerülnek.

### Futtatás publish után

- **macOS**: `publish/osx-arm64/publish/GitRunnerManager.app/Contents/MacOS/GitRunnerManager`
- **Windows**: `publish/win-x64/publish/GitRunnerManager.exe`
- **Linux**: `publish/linux-x64/publish/GitRunnerManager`

### Tech stack

- .NET 9
- Avalonia UI 11.x
- C#
- Services / Stores / Models / Views
- Platform-specifikus szolgáltatások
- Unit tesztek: `src/GitRunnerManager.Tests`

### Bejelentkezéskori indítás

- **Windows**: Registry Run key
- **macOS**: platformfüggő támogatás
- **Linux**: `.desktop` fájl a `~/.config/autostart` mappában

## English

**GitRunnerManager Avalonia** is a Windows, macOS, and Linux desktop app for managing GitHub Actions self-hosted runners. It is the cross-platform C# / .NET 9 and Avalonia UI version of the Swift macOS app.

### Features

- Tray or menu bar status icon
- Multiple runner profiles
- Start, stop, restart, and automatic mode
- Start, stop, and update all runners
- Add runner wizard
- Existing runner folder import
- Repository and organization runner configuration
- Per-runner enabled, auto-start, and automatic mode settings
- Battery and metered-network based pausing
- CPU, memory, process, and active job monitoring
- Runner log viewer and log folder access
- Individual and bulk runner updates
- GitHub account management with personal and organization access
- GitHub OAuth Device Flow and GitHub CLI token fallback
- GitHub Actions dashboard
- Workflow runs, jobs, steps, and statuses
- Runner group and allowed repository display when permissions allow it
- Local runner activity and GitHub job correlation
- Repository filter in the Actions dashboard
- Diagnostic context copy for LLMs
- Markdown and JSON export
- App updates via GitHub Releases
- Stable and preview update channels
- Automatic update checks
- English and Hungarian localization
- Developer diagnostic logs

### Supported platforms

- **Windows 10/11**: system tray icon
- **macOS 11+**: menu bar icon
- **Linux**: AppIndicator-based TrayIcon

On Linux, tray support depends on the desktop environment.

### Runner folders

Default paths:

- macOS: `/Users/<user>/actions-runner`
- Linux: `/home/<user>/actions-runner`
- Windows: `C:\Users\<user>\GitHub\actions-runner`

Multiple runners can be added, imported, or removed from the Runners settings page.

### GitHub integration

The Actions dashboard uses GitHub access to load runner metadata, workflow runs, and job details. Repository runners need repository Actions read access. Organization runner groups and access metadata may require organization admin permissions.

OAuth Device Flow requires a GitHub OAuth App Client ID. If it is not configured, the app can try to use a GitHub CLI token.

### Build

```bash
dotnet build
```

Release build:

```bash
./scripts/build.sh
```

Publish:

```bash
APP_VERSION=1.0.0 ./scripts/publish-macos-arm64.sh
APP_VERSION=1.0.0 ./scripts/publish-windows-x64.sh
APP_VERSION=1.0.0 ./scripts/publish-windows-arm64.sh
APP_VERSION=1.0.0 ./scripts/publish-linux-x64.sh
APP_VERSION=1.0.0 ./scripts/publish-linux-arm64.sh
```

Windows MSIX:

```powershell
.\scripts\publish-windows-msix.ps1
```

Published apps are written to `publish/`.

### Run after publish

- **macOS**: `publish/osx-arm64/publish/GitRunnerManager.app/Contents/MacOS/GitRunnerManager`
- **Windows**: `publish/win-x64/publish/GitRunnerManager.exe`
- **Linux**: `publish/linux-x64/publish/GitRunnerManager`

### Technology stack

- .NET 9
- Avalonia UI 11.x
- C#
- Services / Stores / Models / Views
- Platform-specific services
- Unit tests: `src/GitRunnerManager.Tests`

### Launch at login

- **Windows**: Registry Run key
- **macOS**: platform-dependent support
- **Linux**: `.desktop` file in `~/.config/autostart`

Licenc: MIT
