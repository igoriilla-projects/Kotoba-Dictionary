import AppIntents

enum WidgetFeed: String, AppEnum, CaseIterable {
    case dictionary, learningList
    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Источник")
    static var caseDisplayRepresentations: [WidgetFeed: DisplayRepresentation] = [
        .dictionary: "Словарь", .learningList: "Список обучения"
    ]
}

struct LearningListEntity: AppEntity {
    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Список обучения")
    static var defaultQuery = LearningListQuery()
    let id: String
    let name: String
    var displayRepresentation: DisplayRepresentation { DisplayRepresentation(title: "\(name)") }
}

struct LearningListQuery: EntityStringQuery {
    func entities(for identifiers: [String]) async throws -> [LearningListEntity] {
        try available().filter { identifiers.contains($0.id) }
    }
    func suggestedEntities() async throws -> [LearningListEntity] { try available() }
    func entities(matching string: String) async throws -> [LearningListEntity] {
        try available().filter { $0.name.localizedStandardContains(string) }
    }
    private func available() throws -> [LearningListEntity] {
        [LearningListEntity(id: "inbox", name: "Входящие")] + (try LearningStore().lists()).map { LearningListEntity(id: $0.id, name: $0.name) }
    }
}
