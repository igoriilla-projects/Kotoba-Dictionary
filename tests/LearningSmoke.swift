import Foundation

@main
struct LearningSmoke {
    static func main() throws {
        if CommandLine.arguments.count == 5 && CommandLine.arguments[1] == "--save" {
            let store = try LearningStore(url: URL(fileURLWithPath: CommandLine.arguments[2]), dictionary: DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[3])))
            try store.save(CommandLine.arguments[4]); return
        }
        let dictionaryPath = CommandLine.arguments[1]
        let dictionary = DictionaryStore(url: URL(fileURLWithPath: dictionaryPath))
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("learning.sqlite")
        let store = try LearningStore(url: url, dictionary: dictionary)
        let entries = try dictionary.search("", limit: 50)
        let id = entries[0].id
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!

        // A separate process saves while no companion app is involved.
        let child = Process()
        child.executableURL = URL(fileURLWithPath: CommandLine.arguments[0])
        child.arguments = ["--save", url.path, dictionaryPath, id]
        try child.run(); child.waitUntilExit(); check(child.terminationStatus == 0)
        try store.save(id)
        check(try store.records().count == 1)
        let errors = LockedErrors()
        DispatchQueue.concurrentPerform(iterations: 24) { _ in
            do { try LearningStore(url: url, dictionary: dictionary).save(id) }
            catch { errors.add(error) }
        }
        check(errors.values.isEmpty, "Concurrent saves failed: \(errors.values)")
        check(try store.records(scope: "inbox").count == 1)
        do { try store.save("kanji-9999999"); fatalError("Accepted invalid canonical ID") } catch {}
        let a = try store.createList("A"), b = try store.createList("B")
        try store.membership(id, list: a, included: true)
        try store.membership(id, list: b, included: true)
        check(try store.records(scope: "inbox").isEmpty)
        check(try store.summary(now: now, calendar: calendar).remainingNew == 20)
        _ = try store.start(scope: a, now: now, calendar: calendar)
        _ = try store.reveal(now: now)
        let reopened = try LearningStore(url: url, dictionary: dictionary)
        check(try reopened.session()?.currentID == id && reopened.session()?.revealedAt == now)
        let prediction = ReviewScheduler().next(nil, grade: .easy, at: now).card
        _ = try reopened.rate(id, grade: .easy, now: now, calendar: calendar)
        check(try reopened.records(scope: b).first?.card == prediction)
        check(try reopened.summary(now: now, calendar: calendar).remainingNew == 19)
        try reopened.renameList(a, name: "Renamed")
        try reopened.deleteList(a); try reopened.deleteList(b)
        check(try reopened.records(scope: "inbox").first?.card == prediction)
        try reopened.remove(id); check(try reopened.records().isEmpty)
        try reopened.save(id); check(try reopened.records().first?.card == prediction)
        _ = try reopened.undo(now: now)
        check(try reopened.records().first?.card == nil)
        check(try reopened.summary(now: now, calendar: calendar).remainingNew == 20)
        check(try !reopened.canUndo())

        // A failed schedule write must roll back its history and quota changes.
        let db = try LearningDatabase(url: url)
        try db.run("CREATE TRIGGER fail_rating BEFORE UPDATE OF card ON saved BEGIN SELECT RAISE(ABORT,'injected write failure'); END")
        do { _ = try reopened.rate(id, grade: .easy, now: now, calendar: calendar); fatalError("Expected rollback") } catch {}
        check(try reopened.records().first?.card == nil)
        check(try reopened.summary(now: now, calendar: calendar).remainingNew == 20)
        check(try reopened.session()?.currentID == id)
        try db.run("DROP TRIGGER fail_rating")

        // Global allowance applies across lists; due cards never consume it.
        for entry in entries.dropFirst() { try reopened.save(entry.id, now: now) }
        let left = try reopened.createList("Left"), right = try reopened.createList("Right")
        for (index, entry) in entries.enumerated() { try reopened.membership(entry.id, list: index < 25 ? left : right, included: true) }
        for index in 0..<20 {
            let scope = index < 10 ? left : right
            let session = try reopened.start(scope: scope, now: now, calendar: calendar)
            let current = session.currentID!
            _ = try reopened.reveal(now: now)
            _ = try reopened.rate(current, grade: .easy, now: now, calendar: calendar)
        }
        check(try reopened.summary(now: now, calendar: calendar).remainingNew == 0)
        check(try reopened.start(now: now, calendar: calendar).currentID == nil)
        let tomorrow = calendar.date(byAdding: .day, value: 1, to: now)!
        check(try reopened.summary(now: tomorrow, calendar: calendar).remainingNew == 20)
        let start = calendar.startOfDay(for: now)
        check(try reopened.summary(now: start, calendar: calendar).remainingNew == 0)
        let end = calendar.date(byAdding: .day, value: 1, to: start)!
        check(try reopened.summary(now: end.addingTimeInterval(-0.001), calendar: calendar).remainingNew == 0)
        check(try reopened.summary(now: end, calendar: calendar).remainingNew == 20)
        var tokyo = calendar; tokyo.timeZone = TimeZone(identifier: "Asia/Tokyo")!
        let tokyoNext = tokyo.date(byAdding: .day, value: 1, to: tokyo.startOfDay(for: now))!
        check(try reopened.summary(now: tokyoNext, calendar: tokyo).remainingNew == 20)
        let future = now.addingTimeInterval(16 * 86400)
        let due = try reopened.start(now: future, calendar: calendar, limit: 0)
        check(due.currentID != nil)
        check(try reopened.records().first { $0.id == due.currentID }?.card != nil)
        _ = try reopened.reveal(now: future)
        _ = try reopened.rate(due.currentID!, grade: .again, now: future, calendar: calendar, limit: 0)
        let lapsed = try reopened.records().first { $0.id == due.currentID }!.card!
        check(lapsed.state == .relearning && lapsed.lapses == 1 && lapsed.due == future.addingTimeInterval(300))
        let scheduler = ReviewScheduler()
        check(scheduler.next(nil, grade: .again, at: now).card.due == now.addingTimeInterval(60))
        check(scheduler.next(nil, grade: .good, at: now).card.due == now.addingTimeInterval(600))
        check(prediction.due == now.addingTimeInterval(15 * 86400))
        for grade in ReviewGrade.allCases {
            let first = scheduler.next(lapsed, grade: grade, at: future.addingTimeInterval(300))
            check(first == scheduler.next(lapsed, grade: grade, at: future.addingTimeInterval(300)))
            check(first.card.stability.isFinite && (1...10).contains(first.card.difficulty))
        }
        let denied = directory.appendingPathComponent("missing/learning.sqlite")
        do { _ = try LearningStore(url: denied, dictionary: dictionary); fatalError("Unexpected access") } catch {}
        try FileManager.default.createDirectory(at: denied.deletingLastPathComponent(), withIntermediateDirectories: true)
        let recovered = try LearningStore(url: denied, dictionary: dictionary)
        try recovered.save(id); check(try recovered.contains(id))
        let boundarySession = try recovered.start(now: end.addingTimeInterval(-1), calendar: calendar)
        _ = try recovered.reveal(now: end.addingTimeInterval(-1))
        _ = try recovered.rate(boundarySession.currentID!, grade: .easy, now: end.addingTimeInterval(1), calendar: calendar)
        check(try recovered.summary(now: end.addingTimeInterval(-1), calendar: calendar).remainingNew == 20)
        check(try recovered.summary(now: end.addingTimeInterval(1), calendar: calendar).remainingNew == 19)
        check(try LearningDatabase(url: denied).rows("SELECT fsrs_log FROM history").first?[0] != nil)
        // Simulate reopening a v1 learning database after an app update.
        try db.run("ALTER TABLE history DROP COLUMN fsrs_log")
        try db.run("PRAGMA user_version=1")
        let upgraded = try LearningStore(url: url, dictionary: dictionary)
        check(try upgraded.records().count == entries.count)
        check(try LearningDatabase(url: url).rows("PRAGMA user_version").first?[0] == "2")
        check(try upgraded.records().first { $0.id == due.currentID }?.card == lapsed)
        check(try LearningDatabase(url: url).rows("PRAGMA integrity_check").first?[0] == "ok")
        check(try LearningDatabase(url: url).rows("PRAGMA journal_mode").first?[0] == "wal")
        print("PASS: cross-process/concurrent/idempotent saves, canonical IDs, lists/Inbox, shared and restored progress, deterministic FSRS, atomic rollback, Undo, session recovery, global allowance, midnight/time zones, denial/retry")
    }
}

private final class LockedErrors: @unchecked Sendable {
    let lock = NSLock()
    private var items: [String] = []
    var values: [String] { lock.lock(); defer { lock.unlock() }; return items }
    func add(_ error: Error) { lock.lock(); defer { lock.unlock() }; items.append(error.localizedDescription) }
}

private func check(_ condition: Bool, _ message: String = "Learning assertion failed") { precondition(condition, message) }
