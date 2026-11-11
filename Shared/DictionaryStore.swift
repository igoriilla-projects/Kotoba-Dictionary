import Foundation
import SQLite3

struct DictionaryEntry: Identifiable, Hashable, Sendable, Codable {
    let id: String
    let kind: String
    let text: String
    let reading: String
    let meaning: String
    let detail: String
    let strokes: Int
    let hiragana: String

    var isKanji: Bool { kind == "kanji" }
    var url: URL { URL(string: "yarxi-study://entry/\(id)")! }

    func reading(in style: ReadingStyle) -> String {
        style == .hiragana && !hiragana.isEmpty ? hiragana : reading
    }

    static let preview = DictionaryEntry(
        id: "kanji-76", kind: "kanji", text: "一", reading: "ichi, itsu / hitotsu",
        meaning: "один", detail: "один; первый; весь, целый; одинаковый; единственный", strokes: 1,
        hiragana: "いち, いつ / ひとつ"
    )
}

enum DictionaryError: LocalizedError {
    case missingResource
    case sqlite(String)

    var errorDescription: String? {
        switch self {
        case .missingResource: return "В приложении нет словаря. Переустановите Kotoba."
        case .sqlite(let message): return "Не удалось прочитать словарь: \(message)"
        }
    }
}

/// The app and extension each read their own bundled copy; no network or shared
/// writable container is needed. Each query owns its connection and statement.
struct DictionaryStore: Sendable {
    let url: URL?

    init(url: URL? = Bundle.main.url(forResource: "dictionary", withExtension: "sqlite")) {
        self.url = url
    }

    private func query(_ sql: String, values: [String] = []) throws -> [[String]] {
        guard let url else { throw DictionaryError.missingResource }
        var connection: OpaquePointer?
        guard sqlite3_open_v2(url.path, &connection, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK else {
            let message = connection.map { String(cString: sqlite3_errmsg($0)) } ?? "Не удалось открыть файл"
            sqlite3_close(connection)
            throw DictionaryError.sqlite(message)
        }
        defer { sqlite3_close(connection) }
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(connection, sql, -1, &statement, nil) == SQLITE_OK else {
            throw DictionaryError.sqlite(String(cString: sqlite3_errmsg(connection)))
        }
        defer { sqlite3_finalize(statement) }
        let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)
        for (index, value) in values.enumerated() {
            guard sqlite3_bind_text(statement, Int32(index + 1), value, -1, transient) == SQLITE_OK else {
                throw DictionaryError.sqlite(String(cString: sqlite3_errmsg(connection)))
            }
        }
        var rows: [[String]] = []
        while true {
            let status = sqlite3_step(statement)
            if status == SQLITE_DONE { return rows }
            guard status == SQLITE_ROW else {
                throw DictionaryError.sqlite(String(cString: sqlite3_errmsg(connection)))
            }
            rows.append((0..<sqlite3_column_count(statement)).map { column in
                guard let bytes = sqlite3_column_text(statement, column) else { return "" }
                return String(cString: bytes)
            })
        }
    }

    private let columns = "id, kind, text, reading, meaning, detail, strokes, hiragana"

    private func decode(_ rows: [[String]]) -> [DictionaryEntry] {
        rows.map { DictionaryEntry(id: $0[0], kind: $0[1], text: $0[2], reading: $0[3],
                                   meaning: $0[4], detail: $0[5], strokes: Int($0[6]) ?? 0, hiragana: $0[7]) }
    }

    func random(kind: String? = nil, count: Int = 1, excluding: String = "") throws -> [DictionaryEntry] {
        let filter = kind == nil ? "" : " AND kind = ?"
        let values = kind.map { [excluding, $0] } ?? [excluding]
        return decode(try query("SELECT \(columns) FROM entries WHERE eligible = 1 AND id != ?\(filter) ORDER BY random() LIMIT \(max(1, min(count, 100)))", values: values))
    }

    func search(_ term: String, kind: String? = nil, limit: Int = 100) throws -> [DictionaryEntry] {
        let escaped = term.trimmingCharacters(in: .whitespacesAndNewlines)
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "en_US_POSIX"))
            .replacingOccurrences(of: "\\", with: "\\\\")
            .replacingOccurrences(of: "%", with: "\\%")
            .replacingOccurrences(of: "_", with: "\\_")
        let filter = kind == nil ? "" : " AND kind = ?"
        var values = ["%\(escaped)%"]
        if let kind { values.append(kind) }
        return decode(try query("SELECT \(columns) FROM entries WHERE eligible = 1 AND search_text LIKE ? ESCAPE '\\'\(filter) ORDER BY length(text), source_id LIMIT \(max(1, min(limit, 500)))", values: values))
    }

    func entry(id: String) throws -> DictionaryEntry? {
        decode(try query("SELECT \(columns) FROM entries WHERE id = ? AND eligible = 1", values: [id])).first
    }

    /// Resolve actual dictionary kanji, preserving their first appearance in a
    /// word. Kana, punctuation and repetition marks do not become dead links.
    func kanji(in text: String) throws -> [DictionaryEntry] {
        var seen = Set<String>()
        let characters = text.map(String.init).filter { seen.insert($0).inserted }
        guard !characters.isEmpty else { return [] }
        let placeholders = characters.map { _ in "?" }.joined(separator: ",")
        let matches = decode(try query("SELECT \(columns) FROM entries WHERE eligible = 1 AND kind = 'kanji' AND text IN (\(placeholders))", values: characters))
        let byText = Dictionary(matches.map { ($0.text, $0) }, uniquingKeysWith: { first, _ in first })
        return characters.compactMap { byText[$0] }
    }

    /// Exact spelling containment, independent of search filters and the normal
    /// 100-result search limit. Lazy UI rows can display every matching word.
    func words(containingKanji character: String) throws -> [DictionaryEntry] {
        guard character.count == 1 else { return [] }
        return decode(try query("SELECT \(columns) FROM entries WHERE eligible = 1 AND kind = 'word' AND instr(text, ?) > 0 ORDER BY length(text), source_id", values: [character]))
    }

    func counts() throws -> (kanji: Int, words: Int) {
        let rows = try query("SELECT kind, count(*) FROM entries WHERE eligible = 1 GROUP BY kind")
        return (Int(rows.first { $0[0] == "kanji" }?[1] ?? "0") ?? 0,
                Int(rows.first { $0[0] == "word" }?[1] ?? "0") ?? 0)
    }
}
