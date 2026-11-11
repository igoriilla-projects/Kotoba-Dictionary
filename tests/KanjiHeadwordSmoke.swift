import AppKit
import Foundation

@main
struct KanjiHeadwordSmoke {
    @MainActor static func main() throws {
        _ = NSApplication.shared
        let dictionary = DictionaryStore(url: URL(fileURLWithPath: CommandLine.arguments[1]))
        let word = try dictionary.search("食べ物", kind: "word").first { $0.text == "食べ物" }!
        let kanji = try dictionary.kanji(in: word.text)
        let view = KanjiHeadwordTextView()
        var opened: [String] = []
        view.openKanji = { opened.append($0.text) }
        for (font, width) in [(68.0, 500.0), (136.0, 200.0)] {
            view.configure(entry: word, kanji: kanji, fontSize: font, alignment: .center)
            view.setFrameSize(view.fittingSize(width: width))
            let first = center(of: 0, in: view)
            let kana = center(of: 1, in: view)
            let last = center(of: 2, in: view)
            precondition(view.link(at: first)?.entry.text == "食")
            precondition(view.link(at: kana) == nil, "Kana must not acquire a link or hand cursor")
            precondition(view.link(at: last)?.entry.text == "物")
            precondition(view.link(at: NSPoint(x: 1, y: first.y)) == nil, "Blank space must not activate the nearest glyph")
            view.updateHover(at: first)
            precondition(view.hoveredRange == NSRange(location: 0, length: 1))
            precondition(color(at: 0, in: view) == NSColor(StudyStyle.linkHover))
            precondition(color(at: 1, in: view) == nil && color(at: 2, in: view) == nil, "Neighbors must retain their original colors")
            precondition(view.toolTip?.contains("食") == true)
            view.updateHover(at: last)
            precondition(color(at: 0, in: view) == nil && color(at: 2, in: view) == NSColor(StudyStyle.linkHover))
            view.updateHover(at: kana)
            precondition(view.hoveredRange == nil && color(at: 2, in: view) == nil && view.toolTip == nil)
            view.setSelectedRange(NSRange(location: 0, length: (word.text as NSString).length))
            view.configure(entry: word, kanji: kanji, fontSize: font, alignment: .center)
            precondition(view.selectedRange().length == 3 && view.string == word.text, "Updates must preserve full-word selection")
            let url = URL(string: "kotoba-kanji://entry/\(kanji.last!.id)")!
            precondition(view.textView(view, clickedOnLink: url, at: 2))
            precondition(opened.last == "物", "Activation must route the clicked canonical kanji")
        }
        let repeated = DictionaryEntry(id: "test-repeated", kind: "word", text: "日日", reading: "", meaning: "", detail: "", strokes: 0, hiragana: "")
        let repeatedKanji = try dictionary.kanji(in: repeated.text)
        view.configure(entry: repeated, kanji: repeatedKanji, fontSize: 68, alignment: .center)
        view.setFrameSize(view.fittingSize(width: 400))
        view.updateHover(at: center(of: 1, in: view))
        precondition(view.hoveredRange == NSRange(location: 1, length: 1), "Adjacent identical links must highlight only one occurrence")
        precondition(color(at: 0, in: view) == nil)
        view.updateHover(at: nil)
        precondition(color(at: 1, in: view) == nil)
        print("PASS: native glyph hit testing, isolated hover colors, kana/blank-space exclusion, wrapping at 200%, repeated kanji, full-word selection and click routing")
    }

    @MainActor static func center(of index: Int, in view: KanjiHeadwordTextView) -> NSPoint {
        let manager = view.layoutManager!, container = view.textContainer!
        manager.ensureLayout(for: container)
        let glyphs = manager.glyphRange(forCharacterRange: NSRange(location: index, length: 1), actualCharacterRange: nil)
        let rect = manager.boundingRect(forGlyphRange: glyphs, in: container)
        return NSPoint(x: rect.midX + view.textContainerOrigin.x, y: rect.midY + view.textContainerOrigin.y)
    }

    @MainActor static func color(at index: Int, in view: KanjiHeadwordTextView) -> NSColor? {
        view.layoutManager?.temporaryAttribute(.foregroundColor, atCharacterIndex: index, effectiveRange: nil) as? NSColor
    }
}
