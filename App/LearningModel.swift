import Foundation
import SwiftUI
import WidgetKit

@MainActor
final class LearningModel: ObservableObject {
    @Published var saved: [SavedEntry] = []
    @Published var lists: [LearningList] = []
    @Published var entries: [String: DictionaryEntry] = [:]
    @Published var summary = LearningSummary()
    @Published var session: ReviewSession?
    @Published var reviewing = false
    @Published var canUndo = false
    @Published var error: String?
    @Published var access = "Проверяем доступ к данным обучения…"
    private var store: LearningStore?
    private let probeAccess: Bool
    var dictionary: DictionaryStore { store?.dictionary ?? DictionaryStore() }
    var limit: Int { UserDefaults.standard.object(forKey: "dailyNewLimit") as? Int ?? 20 }
    var current: DictionaryEntry? { session?.currentID.flatMap { entries[$0] } }
    var currentCard: Card? { saved.first { $0.id == session?.currentID }?.card }

    init(store: LearningStore? = nil, probeAccess: Bool = true) {
        self.store = store; self.probeAccess = probeAccess
        refresh()
    }
    func refresh() {
        do {
            if store == nil { store = try LearningStore() }
            guard let store else { return }
            if probeAccess { access = try LearningContainer.probe(role: "app") }
            saved = try store.records(); lists = try store.lists()
            for record in saved where entries[record.id] == nil { entries[record.id] = try store.dictionary.entry(id: record.id) }
            summary = try store.summary(limit: limit)
            session = reviewing ? try store.resume(limit: limit) : try store.session()
            canUndo = try store.canUndo()
            error = nil
        } catch { self.error = error.localizedDescription; access = error.localizedDescription }
    }
    func action(_ body: (LearningStore) throws -> Void) {
        do {
            if store == nil { store = try LearningStore() }
            guard let store else { return }
            try body(store)
            refresh()
            WidgetCenter.shared.reloadTimelines(ofKind: "YarxiStudyWidget")
        } catch { self.error = error.localizedDescription }
    }
    func save(_ id: String) { action { try $0.save(id) } }
    func remove(_ id: String) { action { try $0.remove(id) } }
    func create(_ name: String) { action { _ = try $0.createList(name) } }
    func rename(_ id: String, name: String) { action { try $0.renameList(id, name: name) } }
    func delete(_ id: String) { action { try $0.deleteList(id) } }
    func setMembership(_ id: String, list: String, included: Bool) { action { try $0.membership(id, list: list, included: included) } }
    func start(scope: String? = nil, resume: Bool = false) {
        action { store in
            session = resume ? try store.resume(limit: limit) : try store.start(scope: scope, limit: limit)
            reviewing = true
        }
    }
    func reveal() { action { session = try $0.reveal() } }
    func rate(_ grade: ReviewGrade) {
        guard let id = session?.currentID else { return }
        action { session = try $0.rate(id, grade: grade, limit: limit) }
    }
    func undo() { action { session = try $0.undo(); reviewing = true } }
}
