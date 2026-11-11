import Foundation
import OSLog

actor WidgetDiagnostics {
    static let shared = WidgetDiagnostics()
    private let directory: URL?
    private let logger = Logger(subsystem: "local.yarxi.study.widget", category: "Timeline")

    init(directory: URL? = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first) {
        self.directory = directory
    }

    func record(_ event: String) {
        logger.info("\(event, privacy: .public)")
        guard let directory else { return }
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let url = directory.appendingPathComponent("YarxiWidget.log")
            let data = Data("\(Date().ISO8601Format()) \(event)\n".utf8)
            if let size = try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize, size < 65536 {
                let handle = try FileHandle(forWritingTo: url)
                defer { try? handle.close() }
                try handle.seekToEnd()
                try handle.write(contentsOf: data)
            } else {
                try data.write(to: url, options: .atomic)
            }
        } catch {
            logger.error("Could not write widget diagnostic: \(error.localizedDescription, privacy: .public)")
        }
    }
}
