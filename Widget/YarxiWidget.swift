import AppIntents
import SwiftUI
import WidgetKit

// Compatibility only for the old saved enum fields. The visible font controls
// below use primitive integers: local ad hoc builds cannot fetch the enum
// metadata through linkd without a signing Team ID.
enum WidgetFontSize: String, AppEnum, CaseIterable {
    case percent50, percent75, percent100, percent125, percent150

    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Размер текста")
    static var caseDisplayRepresentations: [WidgetFontSize: DisplayRepresentation] = [
        .percent50: "50%", .percent75: "75%", .percent100: "100% · По умолчанию",
        .percent125: "125%", .percent150: "150%"
    ]

    var scale: Double {
        switch self {
        case .percent50: return 0.5
        case .percent75: return 0.75
        case .percent100: return 1
        case .percent125: return 1.25
        case .percent150: return 1.5
        }
    }
}


struct StudyConfiguration: WidgetConfigurationIntent {
    static var title: LocalizedStringResource = "Настройки обучения"
    static var description = IntentDescription("Выберите содержимое и частоту смены карточек.")

    @Parameter(title: "Источник", default: .dictionary)
    var feed: WidgetFeed?

    @Parameter(title: "Список обучения")
    var learningList: LearningListEntity?

    @Parameter(title: "Показывать", default: .mixed)
    var content: StudyContent

    @Parameter(title: "Таймер обновления", default: .hourly)
    var interval: RefreshInterval

    @Parameter(title: "Формат чтения", default: .hiragana)
    var readingStyle: ReadingStyle?

    @Parameter(title: "Показывать чтение", default: true)
    var showReading: Bool

    @Parameter(title: "Показать перевод", default: true)
    var showTranslation: Bool

    @Parameter(title: "Размер слова / кандзи", default: .percent100)
    var headwordSize: WidgetFontSize?

    @Parameter(title: "Размер чтения", default: .percent100)
    var readingSize: WidgetFontSize?

    @Parameter(title: "Размер перевода", default: .percent100)
    var translationSize: WidgetFontSize?

    @Parameter(title: "Размер слова / кандзи (%)", default: 100, inclusiveRange: (50, 150))
    var headwordPercent: Int

    @Parameter(title: "Размер чтения (%)", default: 100, inclusiveRange: (50, 150))
    var readingPercent: Int

    @Parameter(title: "Размер перевода (%)", default: 100, inclusiveRange: (50, 150))
    var translationPercent: Int

    static var parameterSummary: some ParameterSummary {
        Summary {
            \.$feed
            \.$learningList
            \.$content
            \.$interval
            \.$readingStyle
            \.$showReading
            \.$showTranslation
            \.$headwordPercent
            \.$readingPercent
            \.$translationPercent
        }
    }
}

/// Snapshot resolved values before handing a timeline to the rendering host.
/// The host doesn't need to rehydrate AppIntent parameter wrappers to draw a card.
struct WidgetDisplayOptions: Equatable, Sendable {
    let readingStyle: ReadingStyle
    let showReading: Bool
    let showTranslation: Bool
    let headwordScale: Double
    let readingScale: Double
    let translationScale: Double

    init(configuration: StudyConfiguration) {
        readingStyle = configuration.readingStyle ?? .hiragana
        showReading = configuration.showReading
        showTranslation = configuration.showTranslation
        headwordScale = Self.scale(configuration.headwordPercent)
        readingScale = Self.scale(configuration.readingPercent)
        translationScale = Self.scale(configuration.translationPercent)
    }

    private static func scale(_ percent: Int) -> Double {
        Double(min(150, max(50, percent))) / 100
    }
}

struct NextEntryIntent: AppIntent {
    static var title: LocalizedStringResource = "Следующий кандзи или слово"
    static var openAppWhenRun = false
    @Parameter(title: "План показа") var planKey: String
    @Parameter(title: "Текущая запись") var displayedID: String
    @Parameter(title: "Ключ показа") var token: String
    init() {}
    init(key: String, id: String, token: String) { self.planKey = key; self.displayedID = id; self.token = token }
    func perform() async throws -> some IntentResult {
        let changed = try WidgetSelectionStore().advance(key: planKey, displayedID: displayedID, token: token)
        await WidgetDiagnostics.shared.record("next advanced=\(changed); using WidgetKit's automatic timeline reload")
        return .result()
    }
}

struct SaveEntryIntent: AppIntent {
    static var title: LocalizedStringResource = "Во входящие"
    static var openAppWhenRun = false
    @Parameter(title: "Запись") var entryID: String
    init() {}
    init(id: String) { self.entryID = id }
    func perform() async throws -> some IntentResult {
        _ = try await save(learningAccess: { try LearningStore() }, selection: WidgetSelectionStore())
        return .result()
    }
    @discardableResult func save(learningAccess: () throws -> LearningStore, selection: WidgetSelectionStore, diagnostics: WidgetDiagnostics = .shared) async throws -> Bool {
        do {
            try learningAccess().save(entryID)
            try selection.saveError(nil, id: entryID)
            await diagnostics.record("save committed id=\(entryID)")
            return true
        } catch {
            try selection.saveError(error.localizedDescription, id: entryID)
            await diagnostics.record("save failed id=\(entryID): \(error.localizedDescription)")
            return false
        }
        // The transaction has completed before returning. WidgetKit reloads this
        // widget automatically; provider reads both selection and saved state.
    }
}

struct StudyTimelineEntry: TimelineEntry {
    let date: Date
    let entry: DictionaryEntry?
    let configuration: WidgetDisplayOptions
    var error: String?
    var saved = false
    var learningError: String?
    var planKey = ""
    var selectionToken = ""
}

struct StudyProvider: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> StudyTimelineEntry {
        Task { await WidgetDiagnostics.shared.record("placeholder requested") }
        return StudyTimelineEntry(date: .now, entry: .preview, configuration: WidgetDisplayOptions(configuration: StudyConfiguration()))
    }

    func snapshot(for configuration: StudyConfiguration, in context: Context) async -> StudyTimelineEntry {
        let options = WidgetDisplayOptions(configuration: configuration)
        await WidgetDiagnostics.shared.record("snapshot requested preview=\(context.isPreview)")
        if context.isPreview {
            return StudyTimelineEntry(date: .now, entry: .preview, configuration: options)
        }
        return await makeTimeline(for: configuration, resolvedOptions: options).entries[0]
    }

    func timeline(for configuration: StudyConfiguration, in context: Context) async -> Timeline<StudyTimelineEntry> {
        // Resolve in WidgetKit's callback, before passing through async helpers.
        let options = WidgetDisplayOptions(configuration: configuration)
        await WidgetDiagnostics.shared.record("intent received schema=primitive-percent-v1 headword=\(String(configuration.headwordPercent)) readingSize=\(String(configuration.readingPercent)) translation=\(String(configuration.translationPercent))")
        if #available(macOS 15.2, *) {
            await WidgetDiagnostics.shared.record("font parameter state reading=\(String(describing: configuration.$readingPercent.valueState))")
        }
        return await makeTimeline(for: configuration, resolvedOptions: options)
    }

    func makeTimeline(
        for configuration: StudyConfiguration,
        store: DictionaryStore = DictionaryStore(),
        now: Date = Date(),
        diagnostics: WidgetDiagnostics = .shared,
        selections: WidgetSelectionStore = WidgetSelectionStore(),
        learning: LearningStore? = nil,
        probeHost: Bool = true,
        resolvedOptions: WidgetDisplayOptions? = nil,
        learningAccess: () throws -> LearningStore = { try LearningStore() }
    ) async -> Timeline<StudyTimelineEntry> {
        // Freeze the AppIntent parameters before the first suspension point.
        let options = resolvedOptions ?? WidgetDisplayOptions(configuration: configuration)
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "test"
        await diagnostics.record("configuration version=\(version) headword=\(String(configuration.headwordPercent)) readingSize=\(String(configuration.readingPercent)) translation=\(String(configuration.translationPercent)) scales=\(options.headwordScale),\(options.readingScale),\(options.translationScale)")
        if probeHost {
            do { await diagnostics.record(try LearningContainer.probe(role: "widget")) }
            catch { await diagnostics.record(error.localizedDescription) }
        }
        await diagnostics.record("timeline requested bundle=\(Bundle.main.bundlePath)")
        let interval = configuration.interval.seconds
        do {
            var learningStore = learning
            var learningError: String?
            if learningStore == nil {
                do { learningStore = try learningAccess() }
                catch { learningError = error.localizedDescription }
            }
            let samples = try selections.plan(configuration: configuration, dictionary: store, learning: learningStore, now: now)
            guard !samples.isEmpty else { throw DictionaryError.sqlite("Нет доступных записей для обучения") }
            let key = WidgetSelectionStore.key(configuration)
            var savedIDs = Set<String>()
            do { savedIDs = Set(try learningStore?.records().map(\.id) ?? []) }
            catch { learningError = error.localizedDescription }
            let errors = try selections.saveErrors()
            let entries = samples.map { sample in
                StudyTimelineEntry(date: sample.date, entry: sample.entry, configuration: options,
                                   saved: savedIDs.contains(sample.entry.id),
                                   learningError: errors[sample.entry.id] ?? learningError,
                                   planKey: key, selectionToken: sample.token)
            }
            let policy: TimelineReloadPolicy = interval == 0 ? .never : .after(samples.last!.date.addingTimeInterval(interval))
            await diagnostics.record("timeline ready entries=\(entries.count) interval=\(configuration.interval.rawValue) reading=\(options.readingStyle.rawValue) first=\(entries.first?.entry?.id ?? "none")")
            return Timeline(entries: entries, policy: policy)
        } catch {
            await diagnostics.record("timeline failed: \(error.localizedDescription)")
            let entry = StudyTimelineEntry(date: now, entry: nil, configuration: options, error: error.localizedDescription)
            return Timeline(entries: [entry], policy: .after(now.addingTimeInterval(900)))
        }
    }
}

struct StudyWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let item: StudyTimelineEntry

    var body: some View {
        StudyWidgetCard(item: item, family: family)
            .environment(\.locale, Locale(identifier: "ru_RU"))
    }
}

struct StudyWidgetCard: View {
    let item: StudyTimelineEntry
    let family: WidgetFamily

    private var small: Bool { family == .systemSmall }

    var body: some View {
        Group {
            if small, let entry = item.entry {
                compactCard(entry)
                    .widgetURL(entry.url)
            } else if family == .systemMedium, let entry = item.entry {
                mediumCard(entry)
                    .widgetURL(entry.url)
            } else if let entry = item.entry {
                VStack(alignment: .leading, spacing: 9) {
                    HStack(spacing: 5) {
                        Text("KOTOBA")
                            .font(.system(size: 10, weight: .bold, design: .monospaced))
                            .tracking(2)
                            .fixedSize()
                        Spacer(minLength: 0)
                        Text(entry.isKanji ? "漢字" : "言葉")
                            .font(.system(size: 10))
                            .foregroundStyle(StudyStyle.accent)
                    }
                    .frame(maxWidth: .infinity)
                    .frame(height: small ? 12 : nil)
                    if !small { Spacer(minLength: 0) }
                    HStack(alignment: .center, spacing: 20) {
                        headword(entry, size: family == .systemLarge ? 72 : 54)
                            .frame(maxWidth: entry.isKanji ? 92 * min(item.configuration.headwordScale, 1.25) : .infinity, alignment: .leading)
                        if entry.isKanji {
                            Rectangle().fill(StudyStyle.accent.opacity(0.3)).frame(width: 1, height: 48)
                            VStack(alignment: .leading, spacing: 7) {
                                reading(entry)
                                translation(entry, lines: mediumTranslationLines)
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                    if !entry.isKanji {
                        reading(entry)
                        translation(entry, lines: mediumTranslationLines)
                    }
                    if family == .systemLarge && item.configuration.showTranslation && entry.isKanji {
                        Divider().overlay(StudyStyle.accent.opacity(0.2))
                        Text(entry.detail)
                            .font(.system(size: 14 * item.configuration.translationScale))
                            .lineLimit(item.configuration.translationScale > 1 ? 4 : 6)
                    }
                    Spacer(minLength: 0)
                    footer(entry)
                    .frame(maxWidth: .infinity)
                    .frame(height: small ? 22 : nil)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .widgetURL(entry.url)
                .accessibilityElement(children: .contain)
            } else {
                VStack(alignment: .leading, spacing: small ? 6 : 10) {
                    Text("KOTOBA").font(.system(size: 10, weight: .bold, design: .monospaced)).tracking(2).fixedSize()
                    Text("Откройте Kotoba").font(.caption).fontWeight(.semibold)
                    Text(item.error ?? "Откройте Kotoba, чтобы проверить словарь.").font(.caption).lineLimit(small ? 3 : 6)
                    Spacer(minLength: 0)
                    HStack {
                        Link("Списки", destination: URL(string: "yarxi-study://lists")!)
                        Spacer(minLength: 0)
                        Link("Настройки", destination: URL(string: "yarxi-study://setup")!)
                    }.font(.caption)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
        }
        .foregroundStyle(StudyStyle.ink)
        .containerBackground(StudyStyle.paper, for: .widget)
    }

    private func compactCard(_ entry: DictionaryEntry) -> some View {
        GeometryReader { geometry in
            // Reserve the header/footer before allocating text space. Enlarged
            // translations use one line so interactive controls keep their space.
            let bodyHeight = max(1, geometry.size.height - 12 - 22 - 15)
            let readingHeight = item.configuration.showReading ? 13 * item.configuration.readingScale : 0
            let translationLineHeight = 15 * item.configuration.translationScale
            let translationLines = 44 * item.configuration.headwordScale + readingHeight + 2 * translationLineHeight <= bodyHeight ? 2 : 1
            let translationHeight = item.configuration.showTranslation ? Double(translationLines) * translationLineHeight : 0
            let headwordHeight = max(1, bodyHeight - readingHeight - translationHeight)
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text("KOTOBA")
                        .font(.system(size: 10, weight: .bold, design: .monospaced))
                        .tracking(2)
                    Spacer(minLength: 0)
                    Text(entry.isKanji ? "漢字" : "言葉")
                        .font(.system(size: 10))
                        .foregroundStyle(StudyStyle.accent)
                }
                .frame(width: geometry.size.width, height: 12)
                Text(entry.text)
                    .font(StudyStyle.japanese((entry.isKanji ? 36 : 27) * item.configuration.headwordScale))
                    .lineLimit(1)
                    .minimumScaleFactor(0.25)
                    .frame(width: geometry.size.width, height: headwordHeight, alignment: .leading)
                if item.configuration.showReading {
                    Text(entry.reading(in: item.configuration.readingStyle))
                        .font(.system(size: 11 * item.configuration.readingScale, design: .serif))
                        .foregroundStyle(StudyStyle.accent)
                        .lineLimit(1)
                        .frame(width: geometry.size.width, height: readingHeight, alignment: .leading)
                }
                if item.configuration.showTranslation {
                    Text(entry.meaning)
                        .font(.system(size: 11 * item.configuration.translationScale, weight: .medium))
                        .lineLimit(translationLines)
                        .frame(width: geometry.size.width, height: translationHeight, alignment: .topLeading)
                }
                Spacer(minLength: 0)
                footer(entry)
                .frame(width: geometry.size.width, height: 22)
            }
            .frame(width: geometry.size.width, height: geometry.size.height, alignment: .topLeading)
        }
    }

    private func footer(_ entry: DictionaryEntry) -> some View {
        HStack(spacing: 8) {
            if !small {
                Text(entry.isKanji ? "Черт: \(entry.strokes)" : "ЯПОНСКИЙ · РУССКИЙ")
                    .font(.system(size: 9, weight: .medium, design: .monospaced)).foregroundStyle(StudyStyle.muted)
            }
            if item.learningError != nil {
                Link(destination: URL(string: "yarxi-study://setup")!) { Image(systemName: "gearshape") }
                    .accessibilityLabel("Настройте доступ к данным обучения")
            }
            Spacer(minLength: 0)
            Button(intent: SaveEntryIntent(id: entry.id)) {
                Label(item.saved ? "Сохранено" : item.learningError == nil ? "Сохранить" : "Повторить", systemImage: item.saved ? "checkmark" : "bookmark")
                    .font(.system(size: 10, weight: .medium))
            }.buttonStyle(.plain).foregroundStyle(StudyStyle.accent)
            Button(intent: NextEntryIntent(key: item.planKey, id: entry.id, token: item.selectionToken)) {
                Image(systemName: "arrow.right").font(.system(size: 11, weight: .semibold))
                    .frame(width: 27, height: 22)
                    .background(StudyStyle.accent.opacity(0.12), in: Capsule())
                    .foregroundStyle(StudyStyle.accent)
            }.buttonStyle(.plain).accessibilityLabel("Следующая запись")
        }
    }

    private func mediumCard(_ entry: DictionaryEntry) -> some View {
        GeometryReader { geometry in
            let bodyHeight = max(1, geometry.size.height - 12 - 23 - 12)
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("KOTOBA")
                        .font(.system(size: 10, weight: .bold, design: .monospaced)).tracking(2)
                    Spacer(minLength: 0)
                    Text(entry.isKanji ? "漢字" : "言葉")
                        .font(.system(size: 10)).foregroundStyle(StudyStyle.accent)
                }.frame(height: 12)
                if entry.isKanji {
                    HStack(alignment: .center, spacing: 16) {
                        headword(entry, size: 54)
                            .frame(width: min(110, 92 * item.configuration.headwordScale), height: bodyHeight)
                        Rectangle().fill(StudyStyle.accent.opacity(0.3)).frame(width: 1, height: 48)
                        VStack(alignment: .leading, spacing: 7) {
                            reading(entry)
                            translation(entry, lines: mediumTranslationLines)
                        }.frame(maxWidth: .infinity, maxHeight: bodyHeight, alignment: .leading)
                    }
                    .frame(height: bodyHeight)
                    .clipped()
                } else {
                    let readingHeight = item.configuration.showReading ? 15 * item.configuration.readingScale : 0
                    let translationLineHeight = 18 * item.configuration.translationScale
                    let lines = max(1, min(3, Int((bodyHeight - 32 * item.configuration.headwordScale - readingHeight - 8) / translationLineHeight)))
                    let translationHeight = item.configuration.showTranslation ? Double(lines) * translationLineHeight : 0
                    let headwordHeight = max(1, bodyHeight - readingHeight - translationHeight - 8)
                    VStack(alignment: .leading, spacing: 4) {
                        headword(entry, size: 54).frame(height: headwordHeight)
                        if item.configuration.showReading {
                            Text(entry.reading(in: item.configuration.readingStyle))
                                .font(.system(size: 12 * item.configuration.readingScale, design: .serif))
                                .foregroundStyle(StudyStyle.accent).lineLimit(1)
                                .frame(height: readingHeight, alignment: .leading)
                        }
                        if item.configuration.showTranslation {
                            translation(entry, lines: lines).frame(height: translationHeight, alignment: .topLeading)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: bodyHeight, alignment: .leading)
                    .clipped()
                }
                footer(entry).frame(height: 23)
            }
            .frame(width: geometry.size.width, height: geometry.size.height, alignment: .topLeading)
        }
    }

    private func headword(_ entry: DictionaryEntry, size: CGFloat) -> some View {
        Text(entry.text)
            .font(StudyStyle.japanese(size * item.configuration.headwordScale))
            .lineLimit(family == .systemLarge && !entry.isKanji && item.configuration.headwordScale > 1 ? 2 : 1)
            .minimumScaleFactor(0.25)
            .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder private func reading(_ entry: DictionaryEntry) -> some View {
        if item.configuration.showReading && !entry.reading.isEmpty {
            Text(entry.reading(in: item.configuration.readingStyle))
                .font(.system(size: (small ? 11 : 12) * item.configuration.readingScale, design: .serif))
                .foregroundStyle(StudyStyle.accent)
                .lineLimit(small ? 1 : 2)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    @ViewBuilder private func translation(_ entry: DictionaryEntry, lines: Int) -> some View {
        if item.configuration.showTranslation {
            Text(entry.meaning)
                .font(.system(size: (small ? 11 : 15) * item.configuration.translationScale, weight: .medium))
                .lineLimit(lines)
        }
    }

    private var mediumTranslationLines: Int {
        family == .systemMedium && (item.configuration.readingScale > 1 || item.configuration.translationScale > 1) ? 2 : 3
    }

}

#if !WIDGET_RENDERER
@main
struct YarxiStudyWidget: Widget {
    let kind = "YarxiStudyWidget"

    init() {
        Task {
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown"
            await WidgetDiagnostics.shared.record("extension initialized version=\(version)")
        }
    }

    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: kind, intent: StudyConfiguration.self, provider: StudyProvider()) { entry in
            StudyWidgetView(item: entry)
        }
        .configurationDisplayName("Kotoba")
        .description("Японский понемногу каждый день. Случайные кандзи и слова с переводом на русский.")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge])
    }
}
#endif
