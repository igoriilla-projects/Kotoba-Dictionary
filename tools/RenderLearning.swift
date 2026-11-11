import AppKit
import SwiftUI

@main
struct RenderLearning {
    @MainActor static func main() throws {
        let dictionary = DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[1]))
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = try LearningStore(url: directory.appendingPathComponent("learning.sqlite"), dictionary: dictionary)
        try store.save("kanji-76"); try store.save("word-100"); try store.save("word-53797")
        let list = try store.createList("На каждый день")
        try store.membership("word-100", list: list, included: true)
        let model = LearningModel(store: store, probeAccess: false)
        let output = URL(fileURLWithPath: CommandLine.arguments[2])
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        try render(TodayView(model: model), name: "today", output: output)
        try render(LearningListsView(model: model, open: { _ in }, review: { _ in }), name: "lists", output: output)
        _ = try store.start(); model.reviewing = true; model.refresh()
        try render(TodayView(model: model), name: "review-front", output: output)
        _ = try store.reveal(); model.refresh()
        try render(TodayView(model: model), name: "review-revealed", output: output)
        try render(StudySettingsView(), name: "settings", output: output)
        try render(VStack(alignment: .leading, spacing: 24) {
            CopyHeadwordButton(text: "合図")
            HeadwordCopyLabel(feedback: .copied)
            HeadwordCopyLabel(feedback: .failed)
        }, name: "copy-feedback", output: output)
        let kanji = try dictionary.kanji(in: "合図")[1]
        let relatedWords = try dictionary.words(containingKanji: kanji.text)
        try render(KanjiLookupContent(entry: kanji, words: relatedWords, dictionary: dictionary, learning: model, readingStyle: .hiragana, open: { _ in }), name: "kanji-lookup", output: output)
        try render(ScrollView {
            KanjiWordsContent(kanji: kanji, words: relatedWords, readingStyle: .hiragana, open: { _ in })
        }, name: "kanji-words-inline", output: output)
        let compound = try dictionary.entry(id: "word-100")!
        try render(InteractiveHeadword(entry: compound, dictionary: dictionary, fontSize: 68, openKanji: { _ in }), name: "linked-word", output: output)
        try render(KanjiLookupContent(entry: compound, words: [], dictionary: dictionary, learning: model, readingStyle: .romaji, open: { _ in }), name: "lookup-word", output: output)
        try render(KanjiLookupContent(entry: kanji, words: relatedWords, dictionary: dictionary, learning: model, readingStyle: .hiragana, open: { _ in }), name: "kanji-lookup-large", output: output, scales: [2, 2, 2])
        try render(TodayView(model: model), name: "review-large", output: output, scales: [2, 2, 2])
        try render(LearningListsView(model: model, open: { _ in }, review: { _ in }), name: "lists-large", output: output, scales: [2, 2, 2])
        try render(StudySettingsView(), name: "settings-large", output: output, scales: [2, 2, 2])
        try render(TodayView(model: model), name: "review-large-reading", output: output, scales: [1, 2, 1])
        try store.remove("kanji-76"); try store.remove("word-100")
        _ = try store.start(); _ = try store.reveal(); model.refresh()
        try render(TodayView(model: model), name: "review-long-word-large", output: output, scales: [2, 2, 2])
    }
    @MainActor static func render<V: View>(_ content: V, name: String, output: URL, scales: [Double] = [1, 1, 1]) throws {
        // Volatile preferences keep layout checks independent of real app settings.
        let preferences = UserDefaults(suiteName: "yarxi-preview-\(UUID().uuidString)")!
        preferences.setVolatileDomain([
            "appHeadwordScale": scales[0], "appReadingScale": scales[1], "appTranslationScale": scales[2]
        ], forName: UserDefaults.argumentDomain)
        let view = content.padding(36).frame(width: 740, height: 700)
            .background(StudyStyle.paper).foregroundStyle(StudyStyle.ink)
            .tint(StudyStyle.accent).preferredColorScheme(.light)
            .environment(\.locale, Locale(identifier: "ru_RU"))
            .defaultAppStorage(preferences).id(name)
        // ImageRenderer omits AppKit-backed Form/TextField/ScrollView content.
        // Capture a laid-out native hosting view so these previews include it.
        let hosting = NSHostingView(rootView: view)
        hosting.frame = NSRect(x: 0, y: 0, width: 740, height: 700)
        hosting.layoutSubtreeIfNeeded()
        guard let bitmap = hosting.bitmapImageRepForCachingDisplay(in: hosting.bounds) else { fatalError("Could not render \(name)") }
        hosting.cacheDisplay(in: hosting.bounds, to: bitmap)
        guard let data = bitmap.representation(using: .png, properties: [:]) else { fatalError("Could not encode \(name)") }
        try data.write(to: output.appendingPathComponent("\(name).png"))
        print("Rendered \(name)")
    }
}
