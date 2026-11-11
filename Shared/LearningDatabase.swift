import Foundation
import SQLite3

enum LearningError: LocalizedError {
    case unavailable(String)
    case invalid(String)
    var errorDescription: String? {
        switch self {
        case .unavailable(let reason): return "Нужен доступ к данным обучения. Откройте «Настройки → Настройка виджетов», разрешите доступ к общим данным и нажмите «Повторить». \(reason)"
        case .invalid(let message): return message
        }
    }
}

/// A connection belongs to one operation. IMMEDIATE transactions serialize writers
/// across processes; WAL permits concurrent readers. Never fall back to private data.
final class LearningDatabase {
    private var handle: OpaquePointer?
    init(url: URL) throws {
        guard sqlite3_open_v2(url.path, &handle, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil) == SQLITE_OK else {
            let reason = handle.map { String(cString: sqlite3_errmsg($0)) } ?? "Не удалось открыть общую базу данных."
            sqlite3_close(handle); handle = nil
            throw LearningError.unavailable(reason)
        }
        sqlite3_busy_timeout(handle, 10000)
        do {
            try run("PRAGMA foreign_keys=ON")
            try run("PRAGMA journal_mode=WAL")
            try run("PRAGMA synchronous=FULL")
            try transaction {
                let version = Int(try rows("PRAGMA user_version").first?[0] ?? "0") ?? 0
                guard version <= 2 else { throw LearningError.invalid("Для этой базы обучения нужна более новая версия приложения.") }
                if version == 0 {
                    try run("CREATE TABLE saved(id TEXT PRIMARY KEY, active INTEGER NOT NULL DEFAULT 1, saved_at REAL NOT NULL, card TEXT, due REAL, first_review REAL)")
                    try run("CREATE TABLE lists(id TEXT PRIMARY KEY, name TEXT NOT NULL)")
                    try run("CREATE TABLE memberships(entry_id TEXT NOT NULL REFERENCES saved(id), list_id TEXT NOT NULL REFERENCES lists(id) ON DELETE CASCADE, PRIMARY KEY(entry_id,list_id))")
                    try run("CREATE TABLE history(id INTEGER PRIMARY KEY AUTOINCREMENT, entry_id TEXT NOT NULL REFERENCES saved(id), rating INTEGER NOT NULL, reviewed REAL NOT NULL, was_new INTEGER NOT NULL, before_card TEXT, after_card TEXT NOT NULL, undone INTEGER NOT NULL DEFAULT 0)")
                    try run("CREATE INDEX due_index ON saved(active,due)")
                    try run("CREATE INDEX history_date ON history(reviewed,was_new,undone)")
                    try run("CREATE TABLE state(key TEXT PRIMARY KEY, value TEXT NOT NULL)")
                }
                if version < 2 {
                    try run("ALTER TABLE history ADD COLUMN fsrs_log TEXT")
                    try run("PRAGMA user_version=2")
                }
            }
        } catch { sqlite3_close(handle); handle = nil; throw error }
    }
    deinit { sqlite3_close(handle) }
    private func fail() -> LearningError { .unavailable(String(cString: sqlite3_errmsg(handle))) }
    func rows(_ sql: String, _ values: [String?] = []) throws -> [[String?]] {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK else { throw fail() }
        defer { sqlite3_finalize(statement) }
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        for (index, value) in values.enumerated() {
            let result = value.map { sqlite3_bind_text(statement, Int32(index + 1), $0, -1, transient) }
                ?? sqlite3_bind_null(statement, Int32(index + 1))
            guard result == SQLITE_OK else { throw fail() }
        }
        var result: [[String?]] = []
        while true {
            let status = sqlite3_step(statement)
            if status == SQLITE_DONE { return result }
            guard status == SQLITE_ROW else { throw fail() }
            result.append((0..<sqlite3_column_count(statement)).map { column in
                sqlite3_column_text(statement, column).map { String(cString: $0) }
            })
        }
    }
    func run(_ sql: String, _ values: [String?] = []) throws { _ = try rows(sql, values) }
    func transaction<T>(_ body: () throws -> T) throws -> T {
        try run("BEGIN IMMEDIATE")
        do { let value = try body(); try run("COMMIT"); return value }
        catch { try? run("ROLLBACK"); throw error }
    }
    func value(_ key: String) throws -> String? { try rows("SELECT value FROM state WHERE key=?", [key]).first?.first ?? nil }
    func set(_ key: String, _ value: String) throws { try run("INSERT INTO state VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", [key, value]) }
}

enum LearningContainer {
    static let group = "group.local.yarxi.study"
    private static let launchToken = UUID().uuidString
    static func databaseURL() throws -> URL {
        guard let root = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: group) else {
            throw LearningError.unavailable("Общие данные приложения и виджета недоступны.")
        }
        return root.appendingPathComponent("learning.sqlite")
    }
    /// Only execution in the real signed app AND extension proves entitlement access.
    static func probe(role: String) throws -> String {
        let db = try LearningDatabase(url: databaseURL())
        return try db.transaction {
            if role == "app" {
                try db.set("probe.app", launchToken)
                let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown"
                let acknowledged = try db.value("probe.widgetAck") == launchToken
                let currentVersion = try db.value("probe.widgetVersion") == version
                return acknowledged && currentVersion ? "Доступ подтверждён: приложение ↔ виджет (\(version))" : "Приложению доступны данные. Обновите виджет и снова нажмите «Проверить доступ», чтобы подтвердить общий доступ."
            }
            if let token = try db.value("probe.app") { try db.set("probe.widgetAck", token) }
            try db.set("probe.widget", ISO8601DateFormatter().string(from: Date()))
            try db.set("probe.widgetVersion", Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown")
            return "Виджету доступны данные."
        }
    }
}
