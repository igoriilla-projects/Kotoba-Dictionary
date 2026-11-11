import Foundation

enum StudyRefreshTimer {
    /// Cancellation interrupts sleep immediately when settings, page, or app
    /// activity change. The caller clears the deadline when pausing the timer.
    @MainActor static func run(
        every seconds: TimeInterval,
        scheduled: (Date?) -> Void,
        refresh: () -> Void
    ) async {
        guard seconds > 0, seconds.isFinite else {
            scheduled(nil)
            return
        }
        while !Task.isCancelled {
            scheduled(Date().addingTimeInterval(seconds))
            do {
                try await Task.sleep(for: .seconds(seconds))
            } catch { return }
            guard !Task.isCancelled else { return }
            refresh()
        }
    }
}
