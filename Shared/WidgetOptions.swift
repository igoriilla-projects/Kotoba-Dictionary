import AppIntents

enum StudyContent: String, AppEnum, CaseIterable, Codable {
    case mixed, kanji, words

    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Содержимое")
    static var caseDisplayRepresentations: [StudyContent: DisplayRepresentation] = [
        .mixed: "Кандзи и слова", .kanji: "Только кандзи", .words: "Только слова"
    ]

    var title: String {
        switch self {
        case .mixed: return "Все записи"
        case .kanji: return "Кандзи"
        case .words: return "Слова"
        }
    }

    var databaseKind: String? {
        switch self {
        case .mixed: return nil
        case .kanji: return "kanji"
        case .words: return "word"
        }
    }
}

enum RefreshInterval: String, AppEnum, CaseIterable, Codable {
    case fiveMinutes, quarterHour, halfHour, hourly, sixHours, daily, paused

    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Интервал обновления")
    static var caseDisplayRepresentations: [RefreshInterval: DisplayRepresentation] = [
        .fiveMinutes: "Каждые 5 минут", .quarterHour: "Каждые 15 минут",
        .halfHour: "Каждые 30 минут", .hourly: "Раз в час",
        .sixHours: "Каждые 6 часов", .daily: "Раз в день", .paused: "Вручную"
    ]

    var seconds: TimeInterval {
        switch self {
        case .fiveMinutes: return 300
        case .quarterHour: return 900
        case .halfHour: return 1800
        case .hourly: return 3600
        case .sixHours: return 21600
        case .daily: return 86400
        case .paused: return 0
        }
    }

    var title: String {
        switch self {
        case .fiveMinutes: return "Каждые 5 минут"
        case .quarterHour: return "Каждые 15 минут"
        case .halfHour: return "Каждые 30 минут"
        case .hourly: return "Раз в час"
        case .sixHours: return "Каждые 6 часов"
        case .daily: return "Раз в день"
        case .paused: return "Вручную"
        }
    }

    /// Precompute future cards without dividing by zero for manual-only mode.
    var timelineOffsets: [TimeInterval] {
        guard seconds > 0 else { return [0] }
        let count = max(2, min(96, Int(86400 / seconds)))
        return (0..<count).map { Double($0) * seconds }
    }
}

enum ReadingStyle: String, AppEnum, CaseIterable, Codable {
    case hiragana, romaji

    static var typeDisplayRepresentation = TypeDisplayRepresentation(name: "Формат чтения")
    static var caseDisplayRepresentations: [ReadingStyle: DisplayRepresentation] = [
        .hiragana: "Хирагана · ひらがな", .romaji: "Ромадзи · латиница"
    ]

    var title: String {
        switch self {
        case .hiragana: return "Хирагана · ひらがな"
        case .romaji: return "Ромадзи · латиница"
        }
    }
}
