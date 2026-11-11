import Foundation
import WidgetKit

@main
struct WidgetSmoke {
    @MainActor static func main() async throws {
        guard CommandLine.arguments.count == 2 else { fatalError("Pass the widget bundle's database") }
        let store = DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[1]))
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let selections = WidgetSelectionStore(url: directory.appendingPathComponent("selections.sqlite"))
        let learning = try LearningStore(url: directory.appendingPathComponent("learning.sqlite"), dictionary: store)
        let diagnostics = WidgetDiagnostics(directory: directory)
        let provider = StudyProvider()
        let now = Date(timeIntervalSince1970: 1_800_000_000)

        for content in StudyContent.allCases {
            for interval in RefreshInterval.allCases {
                for style in ReadingStyle.allCases {
                    let configuration = StudyConfiguration()
                    configuration.content = content
                    configuration.interval = interval
                    configuration.readingStyle = style
                    let timeline = await provider.makeTimeline(for: configuration, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
                    precondition(timeline.entries.count == interval.timelineOffsets.count)
                    for (index, item) in timeline.entries.enumerated() {
                        precondition(item.error == nil && item.entry != nil)
                        precondition(item.date == now.addingTimeInterval(interval.timelineOffsets[index]))
                        precondition(item.configuration.readingStyle == style)
                        if let kind = content.databaseKind { precondition(item.entry?.kind == kind) }
                    }
                }
            }
        }

        // Existing widgets serialize the original parameters without readingStyle.
        let legacy = StudyConfiguration()
        legacy.content = .mixed
        legacy.interval = .hourly
        legacy.readingStyle = nil
        legacy.feed = nil
        legacy.learningList = nil
        legacy.headwordPercent = 100
        legacy.readingPercent = 100
        legacy.translationPercent = 100
        legacy.showReading = true
        legacy.showTranslation = false
        let migrated = await provider.makeTimeline(for: legacy, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        precondition(!migrated.entries.isEmpty)
        precondition(migrated.entries.allSatisfy { item in
            item.error == nil && item.entry != nil && item.configuration.readingStyle == .hiragana
                && item.configuration.showReading && !item.configuration.showTranslation
                && item.configuration.headwordScale == 1 && item.configuration.readingScale == 1
                && item.configuration.translationScale == 1
        })

        let failure = await provider.makeTimeline(for: legacy, store: DictionaryStore(url: nil), now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        precondition(failure.entries.count == 1)
        precondition(failure.entries[0].error != nil && failure.entries[0].entry == nil)
        let log = try String(contentsOf: directory.appendingPathComponent("YarxiWidget.log"), encoding: .utf8)
        precondition(log.contains("timeline ready") && log.contains("timeline failed"))
        // Reloads preserve the active card and every future selection token.
        let first = await provider.makeTimeline(for: legacy, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        let displayed = first.entries[0]
        let saveIntent = SaveEntryIntent(id: displayed.entry!.id)
        check(try await saveIntent.save(learningAccess: { learning }, selection: selections, diagnostics: diagnostics))
        check(try await saveIntent.save(learningAccess: { learning }, selection: selections, diagnostics: diagnostics))
        let saved = await provider.makeTimeline(for: legacy, store: store, now: now.addingTimeInterval(1), diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        check(saved.entries[0].saved)
        check(saved.entries.map(\.selectionToken) == first.entries.map(\.selectionToken))
        check(saved.entries[0].entry?.id == displayed.entry?.id)
        check(try selections.advance(key: displayed.planKey, displayedID: displayed.entry!.id, token: displayed.selectionToken, dictionary: store, learning: learning, now: now.addingTimeInterval(2)))
        check(try !selections.advance(key: displayed.planKey, displayedID: displayed.entry!.id, token: displayed.selectionToken, dictionary: store, learning: learning, now: now.addingTimeInterval(3)))
        let advanced = await provider.makeTimeline(for: legacy, store: store, now: now.addingTimeInterval(3), diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        check(advanced.entries[0].selectionToken == first.entries[1].selectionToken)
        check(advanced.entries[0].entry?.id != displayed.entry?.id)
        let outcomes = AdvanceResults()
        let current = advanced.entries[0]
        DispatchQueue.concurrentPerform(iterations: 12) { _ in
            do { outcomes.add(try selections.advance(key: current.planKey, displayedID: current.entry!.id, token: current.selectionToken, dictionary: store, learning: learning, now: now.addingTimeInterval(3))) }
            catch { outcomes.fail(error) }
        }
        check(outcomes.successes == 1 && outcomes.errors.isEmpty)
        let persisted = WidgetSelectionStore(url: directory.appendingPathComponent("selections.sqlite"))
        let reloaded = await provider.makeTimeline(for: legacy, store: store, now: now.addingTimeInterval(4), diagnostics: diagnostics, selections: persisted, learning: learning, probeHost: false)
        check(reloaded.entries[0].selectionToken == advanced.entries[1].selectionToken)

        // Both feeds preserve filters, readings and intervals. Single-card lists
        // still advance exactly once because tokens differ even when IDs match.
        let list = try learning.createList("Widget list")
        let kanji = try store.entry(id: "kanji-76")!
        let word = try store.entry(id: "word-100")!
        try learning.save(kanji.id); try learning.save(word.id)
        try learning.membership(kanji.id, list: list, included: true)
        try learning.membership(word.id, list: list, included: true)
        // Typography is per widget and display-only: changing one size must
        // preserve current/future selections and leave the other sizes at 100%.
        for feed in WidgetFeed.allCases {
            let configuration = StudyConfiguration()
            configuration.feed = feed
            configuration.learningList = feed == .learningList ? LearningListEntity(id: list, name: "Widget list") : nil
            configuration.headwordPercent = 100; configuration.readingPercent = 100; configuration.translationPercent = 100
            let baseline = await provider.makeTimeline(for: configuration, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
            let key = WidgetSelectionStore.key(configuration)
            for size in [50, 75, 100, 125, 150] {
                for field in 0..<3 {
                    configuration.headwordPercent = field == 0 ? size : 100
                    configuration.readingPercent = field == 1 ? size : 100
                    configuration.translationPercent = field == 2 ? size : 100
                    let resized = await provider.makeTimeline(for: configuration, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
                    check(WidgetSelectionStore.key(configuration) == key)
                    check(resized.entries.map(\.selectionToken) == baseline.entries.map(\.selectionToken))
                    check(resized.entries.map { $0.entry?.id } == baseline.entries.map { $0.entry?.id })
                    check(resized.entries.allSatisfy {
                        $0.configuration.headwordScale == (field == 0 ? Double(size) / 100 : 1)
                        && $0.configuration.readingScale == (field == 1 ? Double(size) / 100 : 1)
                        && $0.configuration.translationScale == (field == 2 ? Double(size) / 100 : 1)
                    })
                }
            }
        }
        // The callback's plain-value snapshot must survive later changes to the
        // AppIntent wrappers while asynchronous provider work is in progress.
        let callbackConfiguration = StudyConfiguration()
        callbackConfiguration.readingPercent = 150
        let callbackOptions = WidgetDisplayOptions(configuration: callbackConfiguration)
        callbackConfiguration.readingPercent = 100
        let callbackTimeline = await provider.makeTimeline(for: callbackConfiguration, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false, resolvedOptions: callbackOptions)
        check(callbackTimeline.entries.allSatisfy { $0.configuration.readingScale == 1.5 })
        let numericConfiguration = StudyConfiguration()
        numericConfiguration.readingSize = .percent50 // obsolete saved enum must not override the numeric control
        numericConfiguration.readingPercent = 150
        check(WidgetDisplayOptions(configuration: numericConfiguration).readingScale == 1.5)
        numericConfiguration.headwordPercent = 0
        numericConfiguration.readingPercent = Int.max
        numericConfiguration.translationPercent = -1
        let boundedSizes = WidgetDisplayOptions(configuration: numericConfiguration)
        check(boundedSizes.headwordScale == 0.5 && boundedSizes.readingScale == 1.5 && boundedSizes.translationScale == 0.5)
        for content in StudyContent.allCases {
            for interval in RefreshInterval.allCases {
                for style in ReadingStyle.allCases {
                    let configuration = StudyConfiguration()
                    configuration.feed = .learningList
                    configuration.learningList = LearningListEntity(id: list, name: "Widget list")
                    configuration.content = content; configuration.interval = interval; configuration.readingStyle = style
                    let timeline = await provider.makeTimeline(for: configuration, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
                    check(timeline.entries.count == interval.timelineOffsets.count)
                    check(timeline.entries.allSatisfy { $0.saved && $0.error == nil && [kanji.id,word.id].contains($0.entry!.id) && $0.configuration.readingStyle == style })
                    if let kind = content.databaseKind { check(timeline.entries.allSatisfy { $0.entry?.kind == kind }) }
                    let item = timeline.entries[0]
                    check(try selections.advance(key: item.planKey, displayedID: item.entry!.id, token: item.selectionToken, dictionary: store, learning: learning, now: now.addingTimeInterval(1)))
                    check(try !selections.advance(key: item.planKey, displayedID: item.entry!.id, token: item.selectionToken, dictionary: store, learning: learning, now: now.addingTimeInterval(1)))
                }
            }
        }
        let listConfig = StudyConfiguration()
        listConfig.feed = .learningList
        listConfig.learningList = LearningListEntity(id: list, name: "Widget list")
        try learning.deleteList(list)
        let deleted = await provider.makeTimeline(for: listConfig, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        check(deleted.entries[0].entry == nil && deleted.entries[0].error!.contains("список удалён"))
        let emptyList = try learning.createList("Empty")
        listConfig.learningList = LearningListEntity(id: emptyList, name: "Empty")
        let empty = await provider.makeTimeline(for: listConfig, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        check(empty.entries[0].entry == nil && empty.entries[0].error!.contains("нет подходящих"))
        let noAccess = await provider.makeTimeline(for: legacy, store: store, now: now, diagnostics: diagnostics, selections: selections, probeHost: false, learningAccess: { throw LearningError.unavailable("Test denial") })
        check(noAccess.entries[0].entry != nil && !noAccess.entries[0].saved && noAccess.entries[0].learningError != nil)
        let deniedIntent = SaveEntryIntent(id: noAccess.entries[0].entry!.id)
        check(try await !deniedIntent.save(learningAccess: { throw LearningError.unavailable("Test denied save") }, selection: selections, diagnostics: diagnostics))
        let deniedSave = await provider.makeTimeline(for: legacy, store: store, now: now, diagnostics: diagnostics, selections: selections, probeHost: false, learningAccess: { throw LearningError.unavailable("Test denial") })
        check(deniedSave.entries[0].entry?.id == noAccess.entries[0].entry?.id && !deniedSave.entries[0].saved)
        check(try await deniedIntent.save(learningAccess: { learning }, selection: selections, diagnostics: diagnostics))
        let recovered = await provider.makeTimeline(for: legacy, store: store, now: now, diagnostics: diagnostics, selections: selections, learning: learning, probeHost: false)
        check(recovered.entries[0].saved && recovered.entries[0].learningError == nil)
        print("PASS: 84 provider configurations + 30 independent font variants, legacy defaults, font edits preserving selections, Save stability, stale/duplicate Next rejection, both feeds, deleted/empty lists, denied access/retry and diagnostics")
    }
}

private func check(_ condition: Bool) { precondition(condition) }

private final class AdvanceResults: @unchecked Sendable {
    private let lock = NSLock()
    private(set) var successes = 0
    private(set) var errors: [String] = []
    func add(_ value: Bool) { lock.lock(); defer { lock.unlock() }; if value { successes += 1 } }
    func fail(_ error: Error) { lock.lock(); defer { lock.unlock() }; errors.append(error.localizedDescription) }
}
