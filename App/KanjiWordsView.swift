import SwiftUI

/// Shared loading path for related words in a normal dictionary card.
struct KanjiWordsView: View {
    let kanji: DictionaryEntry
    let dictionary: DictionaryStore
    let readingStyle: ReadingStyle
    let open: (DictionaryEntry) -> Void
    @State private var words: [DictionaryEntry] = []
    @State private var loading = true
    @State private var error: String?

    var body: some View {
        KanjiWordsContent(kanji: kanji, words: words, loading: loading, error: error, readingStyle: readingStyle, open: open)
            .task(id: kanji.id) {
                loading = true
                error = nil
                words = []
                defer { loading = false }
                do { words = try dictionary.words(containingKanji: kanji.text) }
                catch { self.error = error.localizedDescription }
            }
    }
}

/// One list layout and row action for both entry points into a kanji card.
struct KanjiWordsContent: View {
    let kanji: DictionaryEntry
    let words: [DictionaryEntry]
    var loading = false
    var error: String?
    let readingStyle: ReadingStyle
    let open: (DictionaryEntry) -> Void
    private var typography = AppTypography()

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("Слова с кандзи \(kanji.text)").font(.title2)
            if loading {
                ProgressView()
            } else if let error {
                Text(error).foregroundStyle(StudyStyle.accent)
            } else {
                Text("Найдено слов: \(words.count)").font(.caption).foregroundStyle(StudyStyle.muted)
                if words.isEmpty {
                    Text("В словаре нет доступных слов с этим кандзи.")
                        .foregroundStyle(StudyStyle.muted)
                }
                LazyVStack(spacing: 0) {
                    ForEach(words) { word in
                        Button { open(word) } label: {
                            HStack(spacing: 18) {
                                Text(word.text).font(typography.headword(25))
                                    .frame(width: 150, alignment: .leading)
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(word.reading(in: readingStyle)).font(typography.reading(12))
                                        .foregroundStyle(StudyStyle.accent)
                                    Text(word.meaning).font(typography.translation(13)).lineLimit(2)
                                }
                                Spacer()
                                Image(systemName: "chevron.right").font(.caption)
                                    .foregroundStyle(StudyStyle.muted)
                            }
                            .padding(14).frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .contextMenu { CopyHeadwordButton(text: word.text) }
                        Divider()
                    }
                }
                .background(.white.opacity(0.45), in: RoundedRectangle(cornerRadius: 12))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}
