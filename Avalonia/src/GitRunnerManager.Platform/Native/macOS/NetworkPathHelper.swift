import Foundation
import Network

struct NetworkPathSnapshot: Codable {
    let satisfied: Bool
    let expensive: Bool
    let constrained: Bool
    let interfaceType: String
    let interfaceName: String
}

let monitor = NWPathMonitor()
let queue = DispatchQueue(label: "GitRunnerManager.NetworkPathHelper")
let lock = NSLock()
var didEmit = false

func interfaceDescription(for path: NWPath) -> (String, String) {
    let priority: [(NWInterface.InterfaceType, String)] = [
        (.wifi, "Wi-Fi"),
        (.wiredEthernet, "Ethernet"),
        (.cellular, "Cellular"),
        (.loopback, "Loopback"),
        (.other, "Connection")
    ]

    for (type, label) in priority where path.usesInterfaceType(type) {
        let name = path.availableInterfaces.first { $0.type == type }?.name ?? ""
        return (label, name)
    }

    let first = path.availableInterfaces.first
    return ("Connection", first?.name ?? "")
}

func emit(_ path: NWPath) {
    lock.lock()
    if didEmit {
        lock.unlock()
        return
    }
    didEmit = true
    lock.unlock()

    let details = interfaceDescription(for: path)
    let snapshot = NetworkPathSnapshot(
        satisfied: path.status == .satisfied,
        expensive: path.isExpensive,
        constrained: path.isConstrained,
        interfaceType: details.0,
        interfaceName: details.1
    )

    if let data = try? JSONEncoder().encode(snapshot),
       let json = String(data: data, encoding: .utf8) {
        print(json)
        fflush(stdout)
    }

    monitor.cancel()
    exit(0)
}

monitor.pathUpdateHandler = { path in
    emit(path)
}

monitor.start(queue: queue)

DispatchQueue.global().asyncAfter(deadline: .now() + 2.0) {
    lock.lock()
    let shouldExit = !didEmit
    didEmit = true
    lock.unlock()

    if shouldExit {
        monitor.cancel()
        exit(2)
    }
}

dispatchMain()
