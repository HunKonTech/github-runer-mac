import Foundation
import IOKit.ps
import OSLog

struct BatterySnapshot: Sendable {
    let isOnBattery: Bool
    let isCharging: Bool
    let hasBattery: Bool

    var canRun: Bool {
        hasBattery && !isOnBattery
    }
}

final class BatteryMonitor: @unchecked Sendable {
    var onChange: (@Sendable (BatterySnapshot) -> Void)?

    private var isStarted = false
    private let queue = DispatchQueue(label: "github.runner.menu.battery")
    private let logger = Logger(
        subsystem: "com.koncsik.gitrunnermanager",
        category: "battery"
    )

    func start() {
        guard !isStarted else {
            return
        }

        isStarted = true

        let initialSnapshot = currentSnapshot()
        logger.info("Battery monitor started: \(initialSnapshot.description, privacy: .public)")

        notifyChange(initialSnapshot)

        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + 1, repeating: 10)
        timer.setEventHandler { [weak self] in
            self?.checkBattery()
        }
        timer.resume()
    }

    func stop() {
        isStarted = false
    }

    private func checkBattery() {
        let snapshot = currentSnapshot()
        notifyChange(snapshot)
    }

    private func currentSnapshot() -> BatterySnapshot {
        guard let powerSourceInfo = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let powerSourceList = IOPSCopyPowerSourcesList(powerSourceInfo)?.takeRetainedValue() as? [CFTypeRef],
              !powerSourceList.isEmpty
        else {
            return BatterySnapshot(isOnBattery: false, isCharging: false, hasBattery: false)
        }

        for source in powerSourceList {
            if let info = IOPSGetPowerSourceDescription(powerSourceInfo, source)?.takeUnretainedValue() as? [String: Any] {
                let type = info[kIOPSTypeKey as String] as? String

                if type == kIOPSInternalBatteryType as String {
                    let isCharging = (info[kIOPSIsChargingKey as String] as? Bool) ?? false
                    let isPluggedIn = (info[kIOPSPowerSourceStateKey as String] as? String) == (kIOPSACPowerValue as String)

                    return BatterySnapshot(
                        isOnBattery: !isPluggedIn && !isCharging,
                        isCharging: isCharging,
                        hasBattery: true
                    )
                }
            }
        }

        return BatterySnapshot(isOnBattery: false, isCharging: false, hasBattery: false)
    }

    private func notifyChange(_ snapshot: BatterySnapshot) {
        logger.info("Battery state changed: \(snapshot.description, privacy: .public)")

        DispatchQueue.main.async { [weak self] in
            self?.onChange?(snapshot)
        }
    }
}

extension BatterySnapshot: CustomStringConvertible {
    var description: String {
        if !hasBattery {
            return "No battery (desktop Mac)"
        }
        if isCharging {
            return "On battery, charging"
        }
        if isOnBattery {
            return "On battery"
        }
        return "On power adapter"
    }
}