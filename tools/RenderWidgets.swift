import AppKit
import SwiftUI
import WidgetKit

/// Render the production card view without a running WidgetKit host.
@main
struct RenderWidgets {
    @MainActor static func main() throws {
        guard CommandLine.arguments.count == 3 else { fatalError("Pass database path and output directory") }
        let store = DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[1]))
        let output = URL(fileURLWithPath: CommandLine.arguments[2])
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        let examples: [(String, WidgetFamily, Double, Double, String)] = [
            ("small-kanji", .systemSmall, 170, 170, "kanji-34"),
            ("medium-kanji", .systemMedium, 364, 170, "kanji-76"),
            ("medium-long-reading", .systemMedium, 364, 170, "kanji-219"),
            ("medium-long-translation", .systemMedium, 364, 170, "kanji-6055"),
            ("large-kanji", .systemLarge, 364, 382, "kanji-76"),
            ("small-word", .systemSmall, 170, 170, "word-53797"),
            ("medium-word", .systemMedium, 364, 170, "word-53797"),
            ("large-word", .systemLarge, 364, 382, "word-4")
        ]
        for (name, family, width, height, id) in examples {
            for style in ReadingStyle.allCases {
                guard let entry = try store.entry(id: id) else { fatalError("Missing preview entry \(id)") }
                for (variant, headwordSize, readingSize, translationSize) in [
                    ("", 100, 100, 100),
                    ("-text-50", 50, 50, 50),
                    ("-text-150", 150, 150, 150),
                    ("-headword-150", 150, 100, 100),
                    ("-reading-150", 100, 150, 100),
                    ("-translation-150", 100, 100, 150)
                ] {
                    let configuration = StudyConfiguration()
                    configuration.headwordPercent = headwordSize
                    configuration.readingPercent = readingSize
                    configuration.translationPercent = translationSize
                    configuration.readingStyle = style
                    let item = StudyTimelineEntry(date: .now, entry: entry, configuration: WidgetDisplayOptions(configuration: configuration))
                    let filename = (style == .hiragana ? name : "\(name)-romaji") + variant
                    let view = StudyWidgetCard(item: item, family: family)
                        .frame(width: width - 32, height: height - 32)
                        .padding(16)
                        .background(StudyStyle.paper)
                        .clipShape(RoundedRectangle(cornerRadius: 22))
                        .id(filename)
                    let renderer = ImageRenderer(content: view)
                    renderer.proposedSize = ProposedViewSize(width: width, height: height)
                    renderer.scale = 2
                    guard let cg = renderer.cgImage,
                          let data = NSBitmapImageRep(cgImage: cg).representation(using: .png, properties: [:]) else {
                        fatalError("Could not render \(name)")
                    }
                    try data.write(to: output.appendingPathComponent("\(filename).png"))
                    print("Rendered \(filename)")
                }
            }
        }
        for (suffix, family, width, height) in [("small", WidgetFamily.systemSmall, 170.0, 170.0), ("medium", .systemMedium, 364.0, 170.0), ("large", .systemLarge, 364.0, 382.0)] {
            for state in ["saved", "setup", "empty-list"] {
                let configuration = StudyConfiguration()
                var item = StudyTimelineEntry(date: .now, entry: state == "empty-list" ? nil : .preview, configuration: WidgetDisplayOptions(configuration: configuration))
                if state == "saved" { item.saved = true }
                if state == "setup" { item.learningError = "Нужен доступ к данным обучения." }
                if state == "empty-list" { item.error = "В списке нет подходящих записей. Выберите другой в настройках виджета Kotoba." }
                let name = "\(suffix)-\(state)"
                let view = StudyWidgetCard(item: item, family: family)
                    .frame(width: width - 32, height: height - 32).padding(16)
                    .background(StudyStyle.paper).clipShape(RoundedRectangle(cornerRadius: 22)).id(name)
                // Link is AppKit-backed outside WidgetKit; use a native capture
                // for these status cards rather than ImageRenderer's error glyph.
                let hosting = NSHostingView(rootView: view)
                hosting.frame = NSRect(x: 0, y: 0, width: width, height: height)
                hosting.layoutSubtreeIfNeeded()
                guard let bitmap = hosting.bitmapImageRepForCachingDisplay(in: hosting.bounds) else { fatalError("Could not render \(name)") }
                hosting.cacheDisplay(in: hosting.bounds, to: bitmap)
                guard let data = bitmap.representation(using: .png, properties: [:]) else { fatalError("Could not encode \(name)") }
                try data.write(to: output.appendingPathComponent("\(name).png"))
                print("Rendered \(name)")
            }
        }
    }
}
