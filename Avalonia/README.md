# GitHubRunnerTray (Avalonia)

## English

**GitHubRunnerTray** is a cross-platform desktop application for managing a local GitHub Actions self-hosted runner. It provides system tray (Windows/Linux) or menu bar (macOS) integration for controlling the runner without opening Terminal.

This is a cross-platform port of the original [github-runer-mac](https://github.com/HunKonTech/github-runer-mac) Swift app, rewritten in C# / .NET 9 with Avalonia UI.

### Features

- Tray/Menu bar icon showing runner status
- Start/Stop/Automatic control modes
- Activity monitoring (waiting for jobs, working on job)
- Network condition detection (metered/unmetered/offline)
- Battery monitoring with auto-pause on battery
- Resource usage monitoring (CPU, memory)
- Settings window with full configuration
- Localization support (English, Hungarian)
- Automatic updates via GitHub Releases

### Supported Platforms

- **Windows 10/11**: System tray icon (notification area)
- **macOS 11+**: Menu bar icon
- **Linux**: TrayIcon via AppIndicator (best on GNOME/KDE)

> **Linux Note**: Tray support depends on the desktop environment. Most modern Linux distros with GNOME or KDE should work.

### Runner Folder Configuration

The app expects a GitHub Actions runner directory. Default paths:

- macOS: `/Users/<user>/actions-runner`
- Linux: `/home/<user>/actions-runner`
- Windows: `C:\Users\<user>\GitHub\actions-runner`

You can change this in Settings.

### Build Commands

```bash
# Build
cd Avalonia
./scripts/build.sh

# Publish for macOS (arm64)
APP_VERSION=1.0.0 ./scripts/publish-macos-arm64.sh

# Publish for Windows (x64)
APP_VERSION=1.0.0 ./scripts/publish-windows-x64.sh

# Publish for Linux (x64)
APP_VERSION=1.0.0 ./scripts/publish-linux-x64.sh
```

### Technology Stack

- .NET 9
- Avalonia UI 11.x
- CommunityToolkit.Mvvm
- MVVM pattern with dependency injection

### Launch at Login

- **Windows**: Registry Run key
- **macOS**: Unsupported in this version (use Login Items manually)
- **Linux**: `.desktop` file in `~/.config/autostart`

### Known Limitations

- macOS menu bar: TrayIcon click event may not work on all macOS versions
- Linux: Depends on desktop environment AppIndicator support
- Battery monitoring on Linux requires sysfs access

## Magyar

A **GitHubRunnerTray** egy multiplatform asztali alkalmazás helyi GitHub Actions self-hosted runner kezelésére. Rendszertálcás (Windows/Linux) vagy menüsoros (macOS) integrációt biztosít a runner vezérléséhez terminál nyitása nélkül.

Ez az eredeti [github-runer-mac](https://github.com/HunKonTech/github-runer-mac) Swift alkalmazás cross-platform portja, C# / .NET 9 + Avalonia UI-val újraírva.

### Funkciók

- Tálca/menüsor ikon a runner állapotával
- Indítás/Leállítás/Automatikus vezérlési módok
- Aktivitásfigyelés (feladatokra vár, dolgozik)
- Hálózati állapot detektálás (korlátos/korlátlan/offline)
- Akkumulátor figyelés auto-szünettel akkumulátoron
- Erőforrás-használat figyelés (CPU, memória)
- Beállítások ablak teljes konfigurációval
- Lokalizáció támogatás (Angol, Magyar)
- Automatikus frissítések GitHub Releases-en keresztül

### Támogatott Platformok

- **Windows 10/11**: Rendszertálca ikon
- **macOS 11+**: Menüsor ikon
- **Linux**: TrayIcon AppIndicator-en keresztül (legjobb GNOME/KDE-n)

> **Linux Megjegyzés**: A tálca támogatás a desktop környezettől függ. A legtöbb modern Linux disztró GNOME-mal vagy KDE-vel működnie kell.

### Runner Mappa Beállítás

Az alkalmazás egy GitHub Actions runner mappát vár. Alapértelmezett útvonalak:

- macOS: `/Users/<felhasználó>/actions-runner`
- Linux: `/home/<felhasználó>/actions-runner`
- Windows: `C:\Users\<felhasználó>\GitHub\actions-runner`

Módosítható a Beállításokban.

### Build Parancsok

```bash
# Build
cd Avalonia
./scripts/build.sh

# Publish macOS-re (arm64)
APP_VERSION=1.0.0 ./scripts/publish-macos-arm64.sh

# Publish Windows-ra (x64)
APP_VERSION=1.0.0 ./scripts/publish-windows-x64.sh

# Publish Linux-ra (x64)
APP_VERSION=1.0.0 ./scripts/publish-linux-x64.sh
```

### Tech Stack

- .NET 9
- Avalonia UI 11.x
- CommunityToolkit.Mvvm
- MVVM pattern függőség injektálással

### Bejelentkezéskori Indítás

- **Windows**: Registry Run kulcs
- **macOS**: Nem támogatott ebben a verzióban (kézzel állítsd be a Login Items-ben)
- **Linux**: `.desktop` fájl a `~/.config/autostart`-ban

### Ismert Korlátozások

- macOS menüsor: TrayIcon click esemény nem működhet minden macOS verzión
- Linux: A desktop környezet AppIndicator támogatásától függ
- Akkumulátor figyelés Linux-on sysfs hozzáférést igényel

---

Licenc: MIT