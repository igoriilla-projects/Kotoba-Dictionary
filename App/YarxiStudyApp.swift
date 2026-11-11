import AppKit
import SwiftUI
import WidgetKit

#if !APP_RENDERER
@main
struct YarxiStudyApp: App {
    var body: some Scene {
        Window("Kotoba", id: "main") {
            ContentView()
                .handlesExternalEvents(preferring: ["yarxi-study://entry/", "yarxi-study://lists", "yarxi-study://setup"], allowing: ["yarxi-study://entry/", "yarxi-study://lists", "yarxi-study://setup"])
                .preferredColorScheme(.light)
                .environment(\.locale, Locale(identifier: "ru_RU"))
        }
        .defaultSize(width: 960, height: 700)
        .windowResizability(.contentMinSize)

        Settings {
            StudySettingsView()
                .frame(width: 600, height: 680)
                .preferredColorScheme(.light)
                .environment(\.locale, Locale(identifier: "ru_RU"))
        }
    }
}
#endif

@MainActor
final class StudyModel: ObservableObject {
    @Published var entry: DictionaryEntry?
    @Published var results: [DictionaryEntry] = []
    @Published var error: String?
    @Published var counts = (kanji: 0, words: 0)
    let store = DictionaryStore()

    init() {
        do { counts = try store.counts() }
        catch { self.error = error.localizedDescription }
        let savedContent = UserDefaults.standard.string(forKey: "studyContent") ?? "mixed"
        next(StudyContent(rawValue: savedContent) ?? .mixed)
    }

    func next(_ content: StudyContent) {
        do {
            entry = try store.random(kind: content.databaseKind, excluding: entry?.id ?? "").first
            error = nil
        } catch { self.error = error.localizedDescription }
    }

    func search(_ query: String, content: StudyContent) {
        do {
            results = try store.search(query, kind: content.databaseKind)
            error = nil
        } catch { self.error = error.localizedDescription }
    }

    func open(_ url: URL) {
        guard url.scheme == "yarxi-study", url.host == "entry" else { return }
        do {
            guard let entry = try store.entry(id: url.lastPathComponent) else { return }
            self.entry = entry
            error = nil
        } catch { self.error = error.localizedDescription }
    }
}

private enum Page: String, CaseIterable {
    case today = "Today", lists = "My Lists", dictionary = "Dictionary", settings = "Settings"

    var title: String {
        switch self {
        case .today: return "Сегодня"
        case .lists: return "Мои списки"
        case .dictionary: return "Словарь"
        case .settings: return "Настройки"
        }
    }

    var icon: String {
        switch self {
        case .today: return "sun.max"
        case .lists: return "tray.full"
        case .dictionary: return "text.book.closed"
        case .settings: return "gearshape"
        }
    }
}

struct ContentView: View {
    @StateObject private var model = StudyModel()
    @State private var page: Page = .today
    @StateObject private var learning = LearningModel()
    @State private var exploring = false
    @State private var detail = false
    @AppStorage("studyContent") private var content: StudyContent = .mixed
    @AppStorage("readingStyle") private var readingStyle: ReadingStyle = .hiragana
    @AppStorage("refreshInterval") private var refreshInterval: RefreshInterval = .hourly
    private var typography = AppTypography()
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.openWindow) private var openWindow
    @State private var nextRefreshDate: Date?
    @State private var timerRevision = 0
    @State private var query = ""
    @State private var revealed = true
    @State private var lookupEntry: DictionaryEntry?

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            Rectangle().fill(StudyStyle.ink.opacity(0.10)).frame(width: 1)
            VStack(alignment: .leading, spacing: 26) {
                header
                if let error = model.error {
                    ContentUnavailableView("Не удалось загрузить словарь", systemImage: "exclamationmark.triangle", description: Text(error))
                } else {
                    switch page {
                    case .today: TodayView(model: learning)
                    case .lists: LearningListsView(model: learning, open: { entry in
                        model.entry = entry; detail = true; exploring = false; revealed = true; page = .dictionary
                    }, review: { scope in learning.start(scope: scope); page = .today })
                    case .dictionary:
                        HStack {
                            Button("Поиск") { detail = false; exploring = false; model.search(query, content: content) }
                            Button("Обзор") { detail = false; exploring = true; revealed = true }
                        }
                        if exploring || detail { study } else { dictionary }
                    case .settings: StudySettingsView()
                    }
                }
            }
            .padding(36)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .frame(minWidth: 800, minHeight: 640)
        .background(StudyStyle.paper)
        .foregroundStyle(StudyStyle.ink)
        .tint(StudyStyle.accent)
        .sheet(item: $lookupEntry) { kanji in
            KanjiLookupView(entry: kanji, dictionary: model.store, learning: learning, readingStyle: readingStyle)
        }
        .onAppear {
            WidgetCenter.shared.reloadTimelines(ofKind: "YarxiStudyWidget")
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active {
                learning.refresh()
                WidgetCenter.shared.reloadTimelines(ofKind: "YarxiStudyWidget")
            }
        }
        .task {
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(15))
                if Task.isCancelled { break }
                if scenePhase == .active { learning.refresh() }
            }
        }
        .task(id: refreshTaskID) {
            guard refreshInterval.seconds > 0, page == .dictionary, exploring, !learning.reviewing, lookupEntry == nil, scenePhase == .active else {
                nextRefreshDate = nil
                return
            }
            await StudyRefreshTimer.run(every: refreshInterval.seconds) { deadline in
                nextRefreshDate = deadline
            } refresh: {
                model.next(content)
                revealed = true
            }
        }
        .onChange(of: content) { _, selection in
            model.next(selection)
            model.search(query, content: selection)
            revealed = true
        }
        .onOpenURL { url in
            lookupEntry = nil
            if url.host == "lists" { page = .lists; learning.refresh() }
            else if url.host == "setup" { page = .settings }
            else {
                model.open(url)
                page = .dictionary
                exploring = false
                detail = true
            }
            revealed = true
            timerRevision += 1
            openWindow(id: "main")
            NSApplication.shared.activate(ignoringOtherApps: true)
        }
    }

    private var refreshTaskID: String {
        "\(refreshInterval.rawValue)-\(content.rawValue)-\(page.rawValue)-\(exploring)-\(learning.reviewing)-\(lookupEntry != nil)-\(scenePhase == .active)-\(timerRevision)"
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("字")
                .font(StudyStyle.japanese(44))
                .foregroundStyle(StudyStyle.accent)
                .padding(.bottom, 10)
            Text("KOTOBA")
                .font(.system(size: 20, weight: .bold, design: .monospaced))
                .tracking(5)
            Text("Японский понемногу каждый день")
                .font(.system(size: 11))
                .foregroundStyle(StudyStyle.muted)
                .padding(.top, 8)
                .padding(.bottom, 42)
            ForEach(Page.allCases, id: \.self) { destination in
                Button {
                    page = destination
                    if destination == .dictionary { model.search(query, content: content) }
                } label: {
                    Label(destination.title, systemImage: destination.icon)
                        .font(.system(size: 13, weight: page == destination ? .semibold : .regular))
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 11)
                        .background(page == destination ? StudyStyle.accent.opacity(0.09) : .clear,
                                    in: RoundedRectangle(cornerRadius: 8))
                        .foregroundStyle(page == destination ? StudyStyle.accent : StudyStyle.ink)
                }
                .buttonStyle(.plain)
                .padding(.bottom, 5)
            }
            Spacer()
            Rectangle().fill(StudyStyle.ink.opacity(0.12)).frame(height: 1).padding(.bottom, 18)
            Text("ВАШ СЛОВАРЬ")
                .font(.system(size: 9, weight: .semibold, design: .monospaced))
                .tracking(1.3)
                .foregroundStyle(StudyStyle.muted)
            Text("Кандзи: \(model.counts.kanji.formatted(.number.locale(Locale(identifier: "ru_RU"))))")
                .font(.system(size: 13))
                .padding(.top, 10)
            Text("Слов: \(model.counts.words.formatted(.number.locale(Locale(identifier: "ru_RU"))))")
                .font(.system(size: 13))
                .padding(.top, 4)
            Label("Работает без интернета", systemImage: "checkmark.circle.fill")
                .font(.system(size: 10))
                .foregroundStyle(StudyStyle.muted)
                .padding(.top, 18)
        }
        .padding(24)
        .frame(width: 220)
        .background(.white.opacity(0.25))
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 9) {
            Text(page == .today ? "少しずつ · ПОНЕМНОГУ КАЖДЫЙ ДЕНЬ" : "ЯПОНСКО-РУССКИЙ СЛОВАРЬ")
                .font(.system(size: 10, weight: .medium, design: .monospaced))
                .tracking(1.6)
                .foregroundStyle(StudyStyle.muted)
            HStack {
                Text(page == .today ? "Немного практики сегодня." : page.title)
                    .font(.system(size: 30, weight: .regular, design: .serif))
                Spacer()
                if page == .dictionary {
                    Picker("Содержимое", selection: $content) {
                        ForEach(StudyContent.allCases, id: \.self) { Text($0.title).tag($0) }
                    }
                    .labelsHidden()
                    .frame(width: 140)
                }
            }
        }
    }

    private var study: some View {
        VStack(spacing: 18) {
            if let entry = model.entry {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        HStack {
                            Text(entry.isKanji ? "КАНДЗИ / 漢字" : "СЛОВО / 言葉")
                                .font(.system(size: 10, weight: .semibold, design: .monospaced))
                                .tracking(2)
                                .foregroundStyle(StudyStyle.accent)
                            Spacer()
                            if entry.isKanji {
                                Text("Черт: \(entry.strokes)")
                                    .font(.system(size: 11))
                                    .foregroundStyle(StudyStyle.muted)
                            }
                        }
                        InteractiveHeadword(entry: entry, dictionary: model.store, fontSize: typography.headwordSize(entry.isKanji ? 110 : 68), openKanji: { lookupEntry = $0 })
                            .textSelection(.enabled)
                            .multilineTextAlignment(.center)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: .infinity, minHeight: 125, alignment: .center)
                            .padding(.top, 6)
                        CopyHeadwordButton(text: entry.text)
                            .frame(maxWidth: .infinity)
                        Text(entry.reading(in: readingStyle))
                            .font(typography.reading(19, design: .serif))
                            .textSelection(.enabled)
                            .foregroundStyle(StudyStyle.accent)
                            .frame(maxWidth: .infinity)
                            .multilineTextAlignment(.center)
                        Divider().padding(.vertical, 4)
                        if revealed {
                            Text(entry.meaning)
                                .font(typography.translation(23, weight: .medium))
                                .textSelection(.enabled)
                                .fixedSize(horizontal: false, vertical: true)
                            if entry.isKanji && entry.detail != entry.meaning {
                                Text(entry.detail)
                                    .font(typography.translation(14))
                                    .foregroundStyle(StudyStyle.muted)
                                    .textSelection(.enabled)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            if entry.isKanji {
                                Divider().padding(.vertical, 4)
                                KanjiWordsView(kanji: entry, dictionary: model.store, readingStyle: readingStyle, open: { lookupEntry = $0 })
                                    .id(entry.id)
                            }
                        } else {
                            Button("Показать перевод") { revealed = true }
                                .buttonStyle(.bordered)
                                .frame(maxWidth: .infinity, minHeight: 48)
                        }
                    }
                    .padding(30)
                }
                .background(.white.opacity(0.58), in: RoundedRectangle(cornerRadius: 18))
                .overlay(RoundedRectangle(cornerRadius: 18).stroke(StudyStyle.ink.opacity(0.10)))
                if let error = learning.error {
                    Text(error).font(.caption).foregroundStyle(StudyStyle.accent)
                    Button("Повторить подключение") { learning.refresh() }
                }
                HStack {
                    EntrySaveControls(model: learning, entry: entry)
                    Button(revealed ? "Скрыть перевод" : "Показать перевод") { revealed.toggle() }
                        .buttonStyle(.plain)
                        .foregroundStyle(StudyStyle.muted)
                    Spacer()
                    Button {
                        model.next(content)
                        revealed = true
                        timerRevision += 1
                    } label: {
                        Label("Дальше", systemImage: "arrow.right")
                            .padding(.horizontal, 12)
                            .padding(.vertical, 7)
                    }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.space, modifiers: [])
                }
                HStack {
                    Text(readingStyle == .hiragana ? "Чтение хираганой · Перевод на русский" : "Чтение ромадзи · Перевод на русский")
                    Spacer()
                    if let nextRefreshDate {
                        Text("Новая карточка через \(nextRefreshDate, style: .timer)")
                    } else {
                        Text("Автообновление выключено")
                    }
                }
                .font(.system(size: 10))
                .foregroundStyle(StudyStyle.muted)
            }
        }
    }

    private var dictionary: some View {
        VStack(alignment: .leading, spacing: 14) {
            TextField("Поиск по японскому, чтению или переводу…", text: $query)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 15))
                .onChange(of: query) { _, text in model.search(text, content: content) }
            Text("Найдено: \(model.results.count)\(model.results.count == 100 ? " · показаны первые 100" : "")")
                .font(.caption)
                .foregroundStyle(StudyStyle.muted)
            if model.results.isEmpty {
                ContentUnavailableView("Ничего не найдено", systemImage: "magnifyingglass", description: Text("Попробуйте другое написание, чтение или перевод."))
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(model.results) { entry in
                            Button {
                                model.entry = entry
                                revealed = true
                                page = .dictionary
                                exploring = false
                                detail = true
                            } label: {
                                HStack(spacing: 20) {
                                    Text(entry.text)
                                        .font(typography.headword(25))
                                        .lineLimit(1)
                                        .minimumScaleFactor(0.4)
                                        .frame(width: 145, alignment: .leading)
                                    VStack(alignment: .leading, spacing: 5) {
                                        Text(entry.reading(in: readingStyle)).font(typography.reading(12)).foregroundStyle(StudyStyle.accent)
                                        Text(entry.meaning).font(typography.translation(13)).lineLimit(2)
                                    }
                                    Spacer()
                                    Image(systemName: "chevron.right").font(.caption).foregroundStyle(StudyStyle.muted)
                                }
                                .padding(.vertical, 14)
                                .padding(.horizontal, 14)
                                .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .contextMenu {
                                CopyHeadwordButton(text: entry.text)
                            }
                            Divider()
                        }
                    }
                }
                .background(.white.opacity(0.45), in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

}
