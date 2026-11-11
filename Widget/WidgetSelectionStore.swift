import Foundation

struct WidgetSelection: Codable {
    var date: Date
    let entry: DictionaryEntry
    let token: String
}

struct WidgetSelectionPlan: Codable {
    let content: StudyContent
    let interval: RefreshInterval
    let listID: String?
    var selections: [WidgetSelection]
}

/// Stored only in the extension's private container, independently of App Group
/// authorization. Even an app-driven reload reuses current and scheduled cards.
struct WidgetSelectionStore {
    let url: URL
    init(url: URL? = nil) {
        self.url = url ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("widget-selections.sqlite")
    }
    private func db() throws -> LearningDatabase {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        return try LearningDatabase(url: url)
    }
    static func key(_ configuration: StudyConfiguration) -> String {
        // Font sizes affect rendering only. Keep the existing key format so
        // editing typography preserves both current and scheduled selections.
        let values = [configuration.content.rawValue, configuration.interval.rawValue,
                      (configuration.readingStyle ?? .hiragana).rawValue,
                      String(configuration.showReading), String(configuration.showTranslation),
                      (configuration.feed ?? .dictionary).rawValue,
                      configuration.feed == .learningList ? configuration.learningList?.id ?? "missing" : ""]
        return Data(values.joined(separator: "|").utf8).base64EncodedString()
    }
    private func decode(_ raw: String) throws -> WidgetSelectionPlan { try JSONDecoder().decode(WidgetSelectionPlan.self, from: Data(raw.utf8)) }
    private func persist(_ plan: WidgetSelectionPlan, key: String, db: LearningDatabase) throws {
        try db.set(key, String(decoding: JSONEncoder().encode(plan), as: UTF8.self))
    }
    private func samples(content: StudyContent, count: Int, excluding: String, listID: String?, dictionary: DictionaryStore, learning: LearningStore?) throws -> [DictionaryEntry] {
        guard let listID else { return try dictionary.random(kind: content.databaseKind, count: count, excluding: excluding) }
        guard let learning else { throw LearningError.unavailable("Откройте Kotoba, чтобы настроить доступ к данным обучения.") }
        guard try listID == "inbox" || learning.lists().contains(where: { $0.id == listID }) else { throw LearningError.invalid("Этот список удалён. Выберите другой в настройках виджета Kotoba.") }
        let ids = try learning.records(scope: listID).map(\.id)
        let pool = try ids.compactMap { try dictionary.entry(id: $0) }.filter { content.databaseKind == nil || $0.kind == content.databaseKind }
        guard !pool.isEmpty else { throw LearningError.invalid("В списке нет подходящих записей. Сохраните записи или выберите другой список в настройках виджета Kotoba.") }
        var result: [DictionaryEntry] = [], previous = excluding
        for _ in 0..<count {
            let candidates = pool.filter { $0.id != previous }
            let entry = (candidates.isEmpty ? pool : candidates).randomElement()!
            result.append(entry); previous = entry.id
        }
        return result
    }
    func plan(configuration: StudyConfiguration, dictionary: DictionaryStore, learning: LearningStore?, now: Date) throws -> [WidgetSelection] {
        let key = Self.key(configuration), db = try db()
        return try db.transaction {
            let listID: String?
            if configuration.feed == .learningList {
                guard let id = configuration.learningList?.id else { throw LearningError.invalid("Выберите список обучения в настройках виджета Kotoba.") }
                listID = id
                // Validate the selected list even when a cached plan exists.
                _ = try samples(content: configuration.content, count: 1, excluding: "", listID: id, dictionary: dictionary, learning: learning)
            } else { listID = nil }
            var plan = try db.value(key).map(decode) ?? WidgetSelectionPlan(content: configuration.content, interval: configuration.interval, listID: listID, selections: [])
            if let index = plan.selections.lastIndex(where: { $0.date <= now }) { plan.selections.removeFirst(index) }
            if let listID, let learning {
                let ids = Set(try learning.records(scope: listID).map(\.id))
                if plan.selections.contains(where: { !ids.contains($0.entry.id) }) { plan.selections = plan.selections.filter { ids.contains($0.entry.id) } }
            }
            if let first = plan.selections.first {
                // Validate dictionary access rather than letting a cache conceal a broken bundle.
                guard try dictionary.entry(id: first.entry.id) != nil else { throw DictionaryError.missingResource }
                if plan.interval.seconds > 0 && plan.selections.count == 1 && now.timeIntervalSince(first.date) >= plan.interval.seconds { plan.selections = [] }
            }
            let count = configuration.interval.timelineOffsets.count
            let missing = count - plan.selections.count
            if missing > 0 {
                let base = plan.selections.last.map { $0.date.addingTimeInterval(plan.interval.seconds) } ?? now
                let entries = try samples(content: plan.content, count: missing, excluding: plan.selections.last?.entry.id ?? "", listID: listID, dictionary: dictionary, learning: learning)
                for (index, entry) in entries.enumerated() {
                    plan.selections.append(WidgetSelection(date: base.addingTimeInterval(Double(index) * plan.interval.seconds), entry: entry, token: UUID().uuidString))
                }
            }
            try persist(plan, key: key, db: db)
            var selections = plan.selections
            if !selections.isEmpty { selections[0].date = now }
            return selections
        }
    }
    /// Compare both the displayed ID and generation token: a delayed duplicate
    /// cannot consume another card, including one-card learning lists.
    @discardableResult func advance(key: String, displayedID: String, token: String, dictionary: DictionaryStore = DictionaryStore(), learning: LearningStore? = nil, now: Date = Date()) throws -> Bool {
        let db = try db()
        return try db.transaction {
            guard let raw = try db.value(key) else { return false }
            var plan = try decode(raw)
            guard let index = plan.selections.lastIndex(where: { $0.date <= now }),
                  plan.selections[index].entry.id == displayedID, plan.selections[index].token == token else { return false }
            plan.selections.removeFirst(index + 1)
            if plan.selections.isEmpty {
                let source = try learning ?? (plan.listID == nil ? nil : LearningStore())
                let entries = try samples(content: plan.content, count: 1, excluding: displayedID, listID: plan.listID, dictionary: dictionary, learning: source)
                plan.selections = entries.map { WidgetSelection(date: now, entry: $0, token: UUID().uuidString) }
            }
            for index in plan.selections.indices { plan.selections[index].date = now.addingTimeInterval(Double(index) * plan.interval.seconds) }
            try persist(plan, key: key, db: db); return true
        }
    }
    func saveError(_ message: String?, id: String) throws { try db().set("saveError." + id, message ?? "") }
    func saveError(id: String) throws -> String? {
        let value = try db().value("saveError." + id)
        return value?.isEmpty == false ? value : nil
    }
    func saveErrors() throws -> [String: String] {
        Dictionary(uniqueKeysWithValues: try db().rows("SELECT key,value FROM state WHERE key LIKE 'saveError.%' AND value!=''").map {
            (String($0[0]!.dropFirst("saveError.".count)), $0[1]!)
        })
    }
}
