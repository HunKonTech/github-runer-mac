import Foundation
import OSLog

final class RunnerResourceMonitor: @unchecked Sendable {
    typealias UpdateHandler = @MainActor @Sendable (RunnerResourceUsage) -> Void

    private static let runnerProcessNames: Set<String> = [
        "Runner.Listener",
        "Runner.Worker",
    ]

    private var monitorTask: Task<Void, Never>?
    private let logger = Logger(
        subsystem: "com.koncsik.githubrunnermenu",
        category: "resources"
    )

    func start(onUpdate: @escaping UpdateHandler) {
        guard monitorTask == nil else {
            return
        }

        monitorTask = Task.detached(priority: .utility) { [logger] in
            while !Task.isCancelled {
                let usage = Self.measureUsage(logger: logger)
                await onUpdate(usage)

                try? await Task.sleep(for: .seconds(2))
            }
        }
    }

    func stop() {
        monitorTask?.cancel()
        monitorTask = nil
    }

    private static func measureUsage(logger: Logger) -> RunnerResourceUsage {
        do {
            let result = try Shell.run(
                executable: URL(fileURLWithPath: "/bin/ps"),
                arguments: ["-axo", "pid,ppid,pcpu,rss,comm"]
            )

            guard result.status == 0 else {
                logger.error("Resource ps command failed: \(result.stderr, privacy: .public)")
                return .zero
            }

            return usage(from: result.stdout)
        } catch {
            logger.error("Resource usage measurement failed: \(error.localizedDescription, privacy: .public)")
            return .zero
        }
    }

    private static func usage(from output: String) -> RunnerResourceUsage {
        var hasListener = false
        var hasWorker = false
        var cpuPercent = 0.0
        var rssKB = 0.0

        for line in output.split(whereSeparator: \.isNewline).dropFirst() {
            let fields = line.split(maxSplits: 4, whereSeparator: \.isWhitespace)

            guard fields.count == 5 else {
                continue
            }

            let command = String(fields[4])
            let processName = URL(fileURLWithPath: command).lastPathComponent

            guard runnerProcessNames.contains(processName) else {
                continue
            }

            if processName == "Runner.Listener" {
                hasListener = true
            }

            if processName == "Runner.Worker" {
                hasWorker = true
            }

            cpuPercent += Double(fields[2]) ?? 0
            rssKB += Double(fields[3]) ?? 0
        }

        let isRunning = hasListener || hasWorker

        return RunnerResourceUsage(
            isRunning: isRunning,
            isJobActive: hasWorker,
            cpuPercent: isRunning ? cpuPercent : 0,
            memoryMB: isRunning ? rssKB / 1024 : 0
        )
    }
}
