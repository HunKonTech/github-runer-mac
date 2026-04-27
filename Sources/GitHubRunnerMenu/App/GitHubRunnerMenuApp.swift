import AppKit
import Foundation
import OSLog
import SwiftUI

@main
struct GitHubRunnerMenuApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        Window("Settings", id: "settings") {
            SettingsView(
                store: RunnerMenuStore(),
                preferences: AppPreferencesStore.shared,
                updater: AppUpdateService()
            )
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem?
    private var store: RunnerMenuStore?
    private var refreshTask: Task<Void, Never>?
    
    private let logger = Logger(subsystem: "com.koncsik.githubrunnermenu", category: "appDelegate")

    func applicationDidFinishLaunching(_ notification: Notification) {
        logger.info("applicationDidFinishLaunching called")
        
        NSApp.setActivationPolicy(.accessory)
        
        store = RunnerMenuStore()
        setupMenuBar()
        startRefreshLoop()
        
        logger.info("Menu bar setup complete, statusItem: \(self.statusItem != nil ? "exists" : "nil")")
    }

    func applicationWillTerminate(_ notification: Notification) {
        refreshTask?.cancel()
        store = nil
    }

    private func setupMenuBar() {
        logger.info("Setting up menu bar")
        
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        
        guard let button = statusItem?.button else {
            logger.error("Failed to get status item button")
            return
        }
        
        button.image = NSImage(systemSymbolName: "gearshape.fill", accessibilityDescription: "GitHub Runner")
        button.image?.isTemplate = true
        
        updateMenu()
        
        logger.info("Menu bar setup done, button.image: \(button.image != nil ? "set" : "nil")")
    }

    private func startRefreshLoop() {
        refreshTask = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(5))
                self.updateMenu()
            }
        }
    }

    private func updateMenu() {
        guard let store = store else { return }

        let menu = NSMenu()

        menu.addItem(withTitle: AppStrings.appName, action: nil, keyEquivalent: "")
        if let headerItem = menu.items.last {
            headerItem.isEnabled = false
        }

        menu.addItem(NSMenuItem.separator())
        menu.addItem(withTitle: "\(AppStrings.statusRunnerTitle): \(store.runnerStatusText)", action: nil, keyEquivalent: "")
        menu.items.last?.isEnabled = false

        menu.addItem(withTitle: "\(AppStrings.statusActivityTitle): \(store.activityStatusText)", action: nil, keyEquivalent: "")
        menu.items.last?.isEnabled = false

        menu.addItem(withTitle: "\(AppStrings.statusNetworkTitle): \(store.networkStatusText)", action: nil, keyEquivalent: "")
        menu.items.last?.isEnabled = false

        menu.addItem(NSMenuItem.separator())

        let startItem = NSMenuItem(title: AppStrings.buttonManualStart, action: #selector(startRunner), keyEquivalent: "")
        menu.addItem(startItem)

        let stopItem = NSMenuItem(title: AppStrings.buttonManualStop, action: #selector(stopRunner), keyEquivalent: "")
        menu.addItem(stopItem)

        let autoItem = NSMenuItem(title: AppStrings.buttonAutomaticMode, action: #selector(setAutoMode), keyEquivalent: "")
        autoItem.state = store.controlMode == .automatic ? .on : .off
        menu.addItem(autoItem)

        menu.addItem(NSMenuItem.separator())

        let launchItem = NSMenuItem(title: AppStrings.toggleLaunchAtLogin, action: #selector(toggleLaunchAtLogin), keyEquivalent: "")
        launchItem.state = store.launchAtLoginEnabled ? .on : .off
        menu.addItem(launchItem)

        menu.addItem(NSMenuItem.separator())

        let settingsItem = NSMenuItem(title: AppStrings.buttonOpenSettingsWindow, action: #selector(openSettings), keyEquivalent: ",")
        menu.addItem(settingsItem)

        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: AppStrings.buttonQuit, action: #selector(quitApp), keyEquivalent: "q")
        menu.addItem(quitItem)

        statusItem?.menu = menu
    }

    @objc private func startRunner() {
        store?.forceStart()
    }

    @objc private func stopRunner() {
        store?.forceStop()
    }

    @objc private func setAutoMode() {
        store?.useAutomaticMode()
    }

    @objc private func toggleLaunchAtLogin() {
        guard let store = store else { return }
        store.setLaunchAtLogin(!store.launchAtLoginEnabled)
    }

    @objc private func openSettings() {
        guard let store = store else { return }
        SettingsWindowController.shared.show(store: store)
    }

    @objc private func quitApp() {
        NSApplication.shared.terminate(nil)
    }
}