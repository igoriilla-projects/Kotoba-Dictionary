import Foundation

@main
struct DictionarySmoke {
    @MainActor static func main() async throws {
        guard CommandLine.arguments.count == 2 else { fatalError("Pass dictionary.sqlite") }
        let store = DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[1]))
        let counts = try store.counts()
        precondition(counts.kanji == 6286 && counts.words == 55915, "Unexpected study counts")

        let known = try store.entry(id: "kanji-76")
        precondition(known?.text == "一" && known?.meaning == "один")
        precondition(known?.hiragana == "いち, いつ / ひとつ")
        precondition(known?.reading(in: .hiragana) == known?.hiragana)
        precondition(known?.reading(in: .romaji) == known?.reading)
        precondition(known?.url.absoluteString == "yarxi-study://entry/kanji-76")
        let missing = try store.entry(id: "not-an-entry")
        let unresolved = try store.entry(id: "word-4000")
        precondition(missing == nil)
        precondition(unresolved == nil, "Unresolved reference should not be a study card")

        for kind in [nil, "kanji", "word"] as [String?] {
            let batch = try store.random(kind: kind, count: 96, excluding: "kanji-76")
            precondition(batch.count == 96)
            precondition(Set(batch.map(\.id)).count == 96)
            precondition(!batch.contains { $0.id == "kanji-76" })
            precondition(batch.allSatisfy { !$0.text.isEmpty && !$0.meaning.isEmpty })
            if let kind { precondition(batch.allSatisfy { $0.kind == kind }) }
        }

        let japanese = try store.search("合図")
        precondition(japanese.contains { $0.id == "word-100" })
        let hiragana = try store.search("あいず")
        let unvoiced = try store.search("あいす", limit: 500)
        precondition(hiragana.contains { $0.id == "word-100" }, "Hiragana readings must be searchable")
        precondition(!unvoiced.contains { $0.id == "word-100" }, "Kana voicing must remain distinct")
        let russian = try store.search("ТОКИО")
        precondition(!russian.isEmpty, "Russian search must be case insensitive")
        let romanized = try store.search("shō")
        let plainRomanized = try store.search("sho")
        precondition(!romanized.isEmpty && romanized == plainRomanized, "Macrons must be optional in search")
        let injection = try store.search("' OR 1=1 --")
        let wildcard = try store.search("_")
        let filtered = try store.search("", kind: "kanji")
        precondition(injection.isEmpty, "Search must bind parameters")
        precondition(wildcard.allSatisfy { ($0.text + $0.reading + $0.meaning).contains("_") }, "Underscores must not act as wildcards")
        precondition(filtered.allSatisfy(\.isKanji))

        let compoundKanji = try store.kanji(in: "合図")
        precondition(compoundKanji.map(\.text) == ["合", "図"], "Compound links must preserve the word's order")
        let mixedKanji = try store.kanji(in: "食べ物")
        precondition(mixedKanji.map(\.text) == ["食", "物"], "Kana must stay plain text")
        let repeatedKanji = try store.kanji(in: "時々時")
        precondition(repeatedKanji.map(\.text) == ["時"], "Repeated kanji resolve once; repetition marks must not become dead links")
        let kanaOnly = try store.kanji(in: "あいず・ー")
        let emptyKanji = try store.kanji(in: "")
        precondition(kanaOnly.isEmpty && emptyKanji.isEmpty)
        let compoundWords = try store.words(containingKanji: "図")
        precondition(compoundWords.contains { $0.id == "word-100" })
        precondition(compoundWords.allSatisfy { !$0.isKanji && $0.text.contains("図") }, "Related words must match spelling, not readings or glosses")
        let commonKanjiWords = try store.words(containingKanji: "一")
        precondition(commonKanjiWords.count == 901, "Related words must include all matches beyond the usual search limits")
        precondition(Set(commonKanjiWords.map(\.id)).count == commonKanjiWords.count)
        let invalidKanji = try store.words(containingKanji: "' OR 1=1 --")
        let literalWildcard = try store.words(containingKanji: "%")
        precondition(invalidKanji.isEmpty && literalWildcard.isEmpty, "Containment must bind literal characters")

        do {
            _ = try DictionaryStore(url: nil).counts()
            fatalError("Missing resource must throw")
        } catch DictionaryError.missingResource {}
        for interval in RefreshInterval.allCases {
            let offsets = interval.timelineOffsets
            precondition(offsets.first == 0)
            precondition(offsets.count <= 96)
            if interval == .paused {
                precondition(offsets == [0] && interval.seconds == 0)
            } else {
                precondition(interval.seconds >= 300)
                precondition(offsets.count >= 2)
                for index in 1..<offsets.count {
                    precondition(offsets[index] - offsets[index - 1] == interval.seconds)
                }
            }
            precondition(RefreshInterval(rawValue: interval.rawValue) == interval)
        }

        var scheduledDates: [Date?] = []
        var refreshes = 0
        await StudyRefreshTimer.run(every: 0, scheduled: { scheduledDates.append($0) }, refresh: { refreshes += 1 })
        precondition(refreshes == 0 && scheduledDates.count == 1 && scheduledDates[0] == nil)
        scheduledDates = []
        var timer: Task<Void, Never>?
        let start = Date()
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            timer = Task {
                await StudyRefreshTimer.run(every: 0.01, scheduled: { scheduledDates.append($0) }, refresh: {
                    refreshes += 1
                    timer?.cancel()
                    continuation.resume()
                })
            }
        }
        await timer?.value
        precondition(refreshes == 1, "Cancellation must prevent another refresh")
        precondition(scheduledDates.count == 1 && scheduledDates[0]! > start)
        print("PASS: dictionary, kanji drill-down and complete related-word lists, hiragana search, reading formats, refresh schedules, running timer, Off, and cancellation")
    }
}
