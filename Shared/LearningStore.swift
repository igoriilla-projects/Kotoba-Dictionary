import Foundation

struct LearningList: Identifiable, Hashable, Codable {
    let id: String
    var name: String
}

struct SavedEntry: Identifiable {
    let id: String
    let card: Card?
    let lists: Set<String>
}

struct ReviewSession: Codable {
    var scope: String? // nil = all saved entries; inbox = unassigned
    var currentID: String?
    var revealedAt: Date?
    var expectedCard: String?
}

struct LearningSummary {
    var due = 0
    var remainingNew = 20
    var availableNew = 0
}

struct LearningStore {
    let url: URL
    let dictionary: DictionaryStore
    init(url: URL? = nil, dictionary: DictionaryStore = DictionaryStore()) throws {
        self.url = try url ?? LearningContainer.databaseURL()
        self.dictionary = dictionary
        _ = try LearningDatabase(url: self.url)
    }
    private func db() throws -> LearningDatabase { try LearningDatabase(url: url) }
    private func encode<T: Encodable>(_ value: T) throws -> String { String(decoding: try JSONEncoder().encode(value), as: UTF8.self) }
    private func decode<T: Decodable>(_ value: String, as: T.Type) throws -> T { try JSONDecoder().decode(T.self, from: Data(value.utf8)) }

    func save(_ id: String, now: Date = Date()) throws {
        guard try dictionary.entry(id: id) != nil else { throw LearningError.invalid("Этой записи нет в словаре.") }
        let db = try db()
        try db.transaction {
            try db.run("INSERT INTO saved(id,saved_at) VALUES(?,?) ON CONFLICT(id) DO UPDATE SET active=1", [id, String(now.timeIntervalSince1970)])
        }
    }
    func contains(_ id: String) throws -> Bool {
        try !db().rows("SELECT id FROM saved WHERE id=? AND active=1", [id]).isEmpty
    }
    func remove(_ id: String) throws {
        let db = try db()
        try db.transaction {
            try db.run("DELETE FROM memberships WHERE entry_id=?", [id])
            try db.run("UPDATE saved SET active=0 WHERE id=?", [id])
        }
    }
    func lists() throws -> [LearningList] {
        try db().rows("SELECT id,name FROM lists ORDER BY name COLLATE NOCASE,id").map { LearningList(id: $0[0]!, name: $0[1]!) }
    }
    @discardableResult func createList(_ name: String) throws -> String {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw LearningError.invalid("Введите название списка.") }
        let id = UUID().uuidString
        try db().run("INSERT INTO lists VALUES(?,?)", [id, name]); return id
    }
    func renameList(_ id: String, name: String) throws {
        let name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw LearningError.invalid("Введите название списка.") }
        try db().run("UPDATE lists SET name=? WHERE id=?", [name, id])
    }
    func deleteList(_ id: String) throws { try db().run("DELETE FROM lists WHERE id=?", [id]) }
    func membership(_ entry: String, list: String, included: Bool) throws {
        let db = try db()
        try db.transaction {
            guard try !db.rows("SELECT id FROM saved WHERE id=? AND active=1", [entry]).isEmpty else { throw LearningError.invalid("Сначала сохраните запись.") }
            if included { try db.run("INSERT OR IGNORE INTO memberships VALUES(?,?)", [entry, list]) }
            else { try db.run("DELETE FROM memberships WHERE entry_id=? AND list_id=?", [entry, list]) }
        }
    }
    private func records(_ db: LearningDatabase, scope: String? = nil) throws -> [SavedEntry] {
        let memberships = try db.rows("SELECT entry_id,list_id FROM memberships")
        let map = Dictionary(grouping: memberships, by: { $0[0]! }).mapValues { Set($0.map { $0[1]! }) }
        return try db.rows("SELECT id,card FROM saved WHERE active=1 ORDER BY saved_at,id").compactMap { row in
            let id = row[0]!, lists = map[id] ?? []
            if let scope, scope == "inbox" ? !lists.isEmpty : !lists.contains(scope) { return nil }
            return SavedEntry(id: id, card: try row[1].map { try decode($0, as: Card.self) }, lists: lists)
        }
    }
    func records(scope: String? = nil) throws -> [SavedEntry] {
        let db = try db()
        return try db.transaction { try records(db, scope: scope) }
    }

    private func summary(_ db: LearningDatabase, now: Date, calendar: Calendar, limit: Int, scope: String? = nil) throws -> LearningSummary {
        let start = calendar.startOfDay(for: now)
        guard let end = calendar.date(byAdding: .day, value: 1, to: start) else { throw LearningError.invalid("Не удалось определить текущий день для повторения.") }
        let used = Int(try db.rows("SELECT count(*) FROM history WHERE was_new=1 AND undone=0 AND reviewed>=? AND reviewed<?", [String(start.timeIntervalSince1970), String(end.timeIntervalSince1970)]).first?[0] ?? "0") ?? 0
        let all = try records(db, scope: scope)
        return LearningSummary(due: all.filter { $0.card.map { $0.due <= now } ?? false }.count,
                               remainingNew: max(0, limit - used), availableNew: all.filter { $0.card == nil }.count)
    }
    func summary(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent, limit: Int = 20, scope: String? = nil) throws -> LearningSummary {
        let db = try db()
        return try db.transaction { try summary(db, now: now, calendar: calendar, limit: limit, scope: scope) }
    }
    private func nextID(_ db: LearningDatabase, scope: String?, now: Date, calendar: Calendar, limit: Int) throws -> String? {
        let all = try records(db, scope: scope)
        if let due = all.filter({ $0.card.map { $0.due <= now } ?? false }).min(by: { $0.card!.due < $1.card!.due }) { return due.id }
        if try summary(db, now: now, calendar: calendar, limit: limit).remainingNew > 0 { return all.first { $0.card == nil }?.id }
        return nil
    }
    private func persist(_ session: ReviewSession, db: LearningDatabase) throws { try db.set("session", encode(session)) }
    private func cardJSON(_ db: LearningDatabase, id: String?) throws -> String? {
        guard let id else { return nil }
        return try db.rows("SELECT card FROM saved WHERE id=?", [id]).first?[0]
    }
    func session() throws -> ReviewSession? { try db().value("session").map { try decode($0, as: ReviewSession.self) } }
    func start(scope: String? = nil, now: Date = Date(), calendar: Calendar = .autoupdatingCurrent, limit: Int = 20) throws -> ReviewSession {
        let db = try db()
        return try db.transaction {
            let id = try nextID(db, scope: scope, now: now, calendar: calendar, limit: limit)
            let session = ReviewSession(scope: scope, currentID: id, expectedCard: try cardJSON(db, id: id))
            try persist(session, db: db); return session
        }
    }
    func resume(now: Date = Date(), calendar: Calendar = .autoupdatingCurrent, limit: Int = 20) throws -> ReviewSession {
        let db = try db()
        return try db.transaction {
            var session = try db.value("session").map { try decode($0, as: ReviewSession.self) } ?? ReviewSession()
            if let scope = session.scope, scope != "inbox",
               try db.rows("SELECT id FROM lists WHERE id=?", [scope]).isEmpty { session.scope = nil }
            let all = try records(db, scope: session.scope)
            let valid = all.contains { $0.id == session.currentID && ($0.card.map { $0.due <= now } ?? true) }
            let quota = try summary(db, now: now, calendar: calendar, limit: limit).remainingNew
            let currentCard = try cardJSON(db, id: session.currentID)
            if !valid || currentCard != session.expectedCard || (currentCard == nil && quota == 0) {
                session.currentID = try nextID(db, scope: session.scope, now: now, calendar: calendar, limit: limit)
                session.expectedCard = try cardJSON(db, id: session.currentID)
                session.revealedAt = nil
            }
            try persist(session, db: db); return session
        }
    }
    func reveal(now: Date = Date()) throws -> ReviewSession {
        let db = try db()
        return try db.transaction {
            guard let value = try db.value("session") else { throw LearningError.invalid("Сначала начните повторение.") }
            var session = try decode(value, as: ReviewSession.self)
            session.revealedAt = session.revealedAt ?? now
            try persist(session, db: db); return session
        }
    }
    func rate(_ id: String, grade: ReviewGrade, now: Date = Date(), calendar: Calendar = .autoupdatingCurrent, limit: Int = 20) throws -> ReviewSession {
        let db = try db()
        return try db.transaction {
            guard let raw = try db.value("session") else { throw LearningError.invalid("Сначала начните повторение.") }
            let session = try decode(raw, as: ReviewSession.self)
            guard session.currentID == id, let revealed = session.revealedAt,
                  try records(db, scope: session.scope).contains(where: { $0.id == id }) else { throw LearningError.invalid("Покажите перевод текущей карточки перед оценкой.") }
            let before = try cardJSON(db, id: id)
            guard before == session.expectedCard else { throw LearningError.invalid("Карточка изменилась. Продолжите повторение, чтобы обновить её.") }
            let card = try before.map { try decode($0, as: Card.self) }
            if try card == nil && summary(db, now: now, calendar: calendar, limit: limit).remainingNew == 0 { throw LearningError.invalid("Лимит новых записей на сегодня исчерпан.") }
            let result = ReviewScheduler().next(card, grade: grade, at: revealed)
            let after = try encode(result.card)
            try db.run("INSERT INTO history(entry_id,rating,reviewed,was_new,before_card,after_card,fsrs_log) VALUES(?,?,?,?,?,?,?)", [id, String(grade.rawValue), String(now.timeIntervalSince1970), card == nil ? "1" : "0", before, after, try encode(result.log)])
            let historyID = try db.rows("SELECT last_insert_rowid()").first![0]!
            try db.set("scheduler", "swift-fsrs/v5.0.0; retention=0.9; fuzz=false; shortTerm=true")
            try db.set("undo", historyID)
            try db.set("undo.session", raw)
            try db.run("UPDATE saved SET card=?,due=?,first_review=coalesce(first_review,?) WHERE id=?", [after, String(result.card.due.timeIntervalSince1970), String(now.timeIntervalSince1970), id])
            let next = try nextID(db, scope: session.scope, now: now, calendar: calendar, limit: limit)
            let updated = ReviewSession(scope: session.scope, currentID: next, expectedCard: try cardJSON(db, id: next))
            try persist(updated, db: db); return updated
        }
    }
    func canUndo() throws -> Bool { try db().value("undo").map { !$0.isEmpty } ?? false }
    func undo(now: Date = Date()) throws -> ReviewSession {
        let db = try db()
        return try db.transaction {
            guard let undo = try db.value("undo"), !undo.isEmpty,
                  let row = try db.rows("SELECT entry_id,before_card,after_card,was_new FROM history WHERE id=? AND undone=0", [undo]).first else { throw LearningError.invalid("Нет оценки, которую можно отменить.") }
            let id = row[0]!
            guard try cardJSON(db, id: id) == row[2] else { throw LearningError.invalid("После этой оценки запись уже изменилась.") }
            let card = try row[1].map { try decode($0, as: Card.self) }
            try db.run("UPDATE saved SET card=?,due=?,first_review=CASE WHEN ?='1' THEN NULL ELSE first_review END WHERE id=?", [row[1], card.map { String($0.due.timeIntervalSince1970) }, row[3], id])
            try db.run("UPDATE history SET undone=1 WHERE id=?", [undo]); try db.set("undo", "")
            var session = try db.value("undo.session").map { try decode($0, as: ReviewSession.self) } ?? ReviewSession()
            session.currentID = id; session.revealedAt = now; session.expectedCard = row[1]
            try persist(session, db: db); return session
        }
    }
}
