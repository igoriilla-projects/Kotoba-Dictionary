import AppKit
import SwiftUI

/// One selectable native text view with links and hover regions for individual
/// dictionary kanji. Kana remain ordinary selectable text.
struct InteractiveHeadword: View {
    let entry: DictionaryEntry
    let dictionary: DictionaryStore
    let fontSize: CGFloat
    var alignment: NSTextAlignment = .center
    let openKanji: (DictionaryEntry) -> Void
    @State private var kanji: [DictionaryEntry] = []

    var body: some View {
        NativeKanjiHeadword(entry: entry, kanji: kanji, fontSize: fontSize, alignment: alignment, openKanji: openKanji)
            .task(id: entry.id) {
                kanji = entry.isKanji ? [] : (try? dictionary.kanji(in: entry.text)) ?? []
            }
    }
}

/// Dictionary exploration stays in a sheet so closing it returns to the exact
/// source card, including an unfinished review. Navigation never rates a card.
struct KanjiLookupView: View {
    let entry: DictionaryEntry
    let dictionary: DictionaryStore
    @ObservedObject var learning: LearningModel
    let readingStyle: ReadingStyle
    @State private var path: [DictionaryEntry] = []
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                if !path.isEmpty {
                    Button { path.removeLast() } label: {
                        Label("Назад", systemImage: "chevron.left")
                    }
                }
                Label("Кандзи и слова", systemImage: "text.book.closed")
                    .font(.headline)
                Spacer()
                Button("Готово") { dismiss() }.keyboardShortcut(.cancelAction)
            }
            .padding(20)
            Divider()
            page(path.last ?? entry).id(path.last?.id ?? entry.id)
        }
        .frame(minWidth: 620, idealWidth: 720, minHeight: 560, idealHeight: 700)
        .background(StudyStyle.paper)
        .foregroundStyle(StudyStyle.ink)
        .tint(StudyStyle.accent)
    }

    private func page(_ entry: DictionaryEntry) -> some View {
        KanjiLookupPage(entry: entry, dictionary: dictionary, learning: learning, readingStyle: readingStyle,
                        open: { path.append($0) })
    }
}

private struct KanjiLookupPage: View {
    let entry: DictionaryEntry
    let dictionary: DictionaryStore
    @ObservedObject var learning: LearningModel
    let readingStyle: ReadingStyle
    let open: (DictionaryEntry) -> Void
    @State private var words: [DictionaryEntry] = []
    @State private var loading = true
    @State private var error: String?

    var body: some View {
        KanjiLookupContent(entry: entry, words: words, loading: loading, error: error,
                           dictionary: dictionary, learning: learning, readingStyle: readingStyle, open: open)
            .task(id: entry.id) {
                loading = true
                defer { loading = false }
                do {
                    words = entry.isKanji ? try dictionary.words(containingKanji: entry.text) : []
                    error = nil
                } catch { self.error = error.localizedDescription }
            }
    }
}

struct KanjiLookupContent: View {
    let entry: DictionaryEntry
    let words: [DictionaryEntry]
    var loading = false
    var error: String?
    let dictionary: DictionaryStore
    @ObservedObject var learning: LearningModel
    let readingStyle: ReadingStyle
    let open: (DictionaryEntry) -> Void
    private var typography = AppTypography()

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 14) {
                    InteractiveHeadword(entry: entry, dictionary: dictionary, fontSize: typography.headwordSize(entry.isKanji ? 76 : 46), alignment: .left, openKanji: open)
                        .fixedSize(horizontal: false, vertical: true)
                    Text(entry.reading(in: readingStyle))
                        .font(typography.reading(16, design: .serif))
                        .foregroundStyle(StudyStyle.accent).textSelection(.enabled)
                    Text(entry.meaning).font(typography.translation(20, weight: .medium))
                        .textSelection(.enabled)
                    if entry.detail != entry.meaning {
                        Text(entry.detail).font(typography.translation(14))
                            .foregroundStyle(StudyStyle.muted).textSelection(.enabled)
                    }
                    if entry.isKanji {
                        Text("Черт: \(entry.strokes)").font(.caption).foregroundStyle(StudyStyle.muted)
                    }
                    HStack {
                        CopyHeadwordButton(text: entry.text)
                        Spacer()
                        EntrySaveControls(model: learning, entry: entry)
                    }
                    if let error = learning.error {
                        Text(error).font(.caption).foregroundStyle(StudyStyle.accent)
                        Button("Повторить подключение") { learning.refresh() }
                    }
                }
                .padding(24)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(.white.opacity(0.58), in: RoundedRectangle(cornerRadius: 16))
                if entry.isKanji {
                    KanjiWordsContent(kanji: entry, words: words, loading: loading, error: error, readingStyle: readingStyle, open: open)
                }
            }.padding(24)
        }
        .background(StudyStyle.paper)
    }
}
