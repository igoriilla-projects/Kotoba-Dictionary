import AppKit
import SwiftUI

/// AppKit supplies the same glyph layout for drawing, selection, pointer regions
/// and hit testing. Hover never guesses a character from the word's full bounds.
struct NativeKanjiHeadword: NSViewRepresentable {
    let entry: DictionaryEntry
    let kanji: [DictionaryEntry]
    let fontSize: CGFloat
    let alignment: NSTextAlignment
    let openKanji: (DictionaryEntry) -> Void

    func makeNSView(context: Context) -> KanjiHeadwordTextView {
        KanjiHeadwordTextView()
    }

    func updateNSView(_ view: KanjiHeadwordTextView, context: Context) {
        view.openKanji = openKanji
        view.configure(entry: entry, kanji: kanji, fontSize: fontSize, alignment: alignment)
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView view: KanjiHeadwordTextView, context: Context) -> CGSize? {
        view.fittingSize(width: proposal.width)
    }
}

final class KanjiHeadwordTextView: NSTextView, NSTextViewDelegate {
    var openKanji: (DictionaryEntry) -> Void = { _ in }
    private var entryID = ""
    private var entries: [String: DictionaryEntry] = [:]
    private var configuredFontSize: CGFloat = 0
    private var configuredAlignment: NSTextAlignment = .left
    private var tracking: NSTrackingArea?
    private(set) var hoveredRange: NSRange?
    private var flashRange: NSRange?
    private var flashTask: Task<Void, Never>?

    init() {
        let storage = NSTextStorage()
        let manager = NSLayoutManager()
        let container = NSTextContainer(size: NSSize(width: 600, height: CGFloat.greatestFiniteMagnitude))
        storage.addLayoutManager(manager)
        manager.addTextContainer(container)
        super.init(frame: .zero, textContainer: container)
        delegate = self
        isEditable = false
        isSelectable = true
        isRichText = true
        allowsUndo = false
        drawsBackground = false
        focusRingType = .none
        textContainerInset = .zero
        container.lineFragmentPadding = 0
        container.widthTracksTextView = false
        container.heightTracksTextView = false
        isHorizontallyResizable = false
        isVerticallyResizable = false
        // Preserve each run's own foreground color, including temporary hover
        // attributes, instead of imposing one global color on every link.
        linkTextAttributes = [.underlineStyle: 0, .cursor: NSCursor.pointingHand]
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func configure(entry: DictionaryEntry, kanji: [DictionaryEntry], fontSize: CGFloat, alignment: NSTextAlignment) {
        let resolved = Dictionary(kanji.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        guard entryID != entry.id || string != entry.text || configuredFontSize != fontSize ||
                configuredAlignment != alignment || Set(entries.keys) != Set(resolved.keys) else { return }
        let keepSelection = entryID == entry.id
        let selection = selectedRanges
        updateHover(at: nil)
        clearFlash()
        entryID = entry.id
        entries = resolved
        configuredFontSize = fontSize
        configuredAlignment = alignment
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = alignment
        let font = NSFont(name: "Hiragino Mincho ProN", size: fontSize) ?? NSFont.systemFont(ofSize: fontSize)
        let text = NSMutableAttributedString(string: entry.text, attributes: [
            .font: font, .foregroundColor: NSColor(StudyStyle.ink), .paragraphStyle: paragraph
        ])
        let byText = Dictionary(kanji.map { ($0.text, $0) }, uniquingKeysWith: { first, _ in first })
        var offset = 0
        for character in entry.text {
            let character = String(character)
            let range = NSRange(location: offset, length: character.utf16.count)
            if !entry.isKanji, let target = byText[character] {
                text.addAttributes([
                    .link: URL(string: "kotoba-kanji://entry/\(target.id)")!,
                    .foregroundColor: NSColor(StudyStyle.accent), .underlineStyle: 0,
                    .toolTip: "Открыть \(target.text): значение и слова с этим кандзи"
                ], range: range)
            }
            offset += range.length
        }
        textStorage?.setAttributedString(text)
        if keepSelection {
            selectedRanges = selection.filter { NSMaxRange($0.rangeValue) <= text.length }
        } else {
            setSelectedRange(NSRange(location: 0, length: 0))
        }
        setAccessibilityLabel(entry.text)
        setAccessibilityHelp("Кандзи можно открыть нажатием. Текст можно выделить и скопировать.")
        invalidateIntrinsicContentSize()
        window?.invalidateCursorRects(for: self)
    }

    func fittingSize(width proposedWidth: CGFloat?) -> CGSize {
        let naturalWidth = ceil(attributedString().size().width)
        let width = max(1, proposedWidth ?? naturalWidth)
        // NSTextView can resynchronize its container to the current frame while
        // SwiftUI is still proposing a *new* width. Measure independently so
        // wrapping and height never lag a resize/font change by one layout pass.
        let storage = NSTextStorage(attributedString: attributedString())
        let manager = NSLayoutManager()
        let container = NSTextContainer(size: NSSize(width: width, height: CGFloat.greatestFiniteMagnitude))
        container.lineFragmentPadding = 0
        storage.addLayoutManager(manager)
        manager.addTextContainer(container)
        manager.ensureLayout(for: container)
        let used = manager.usedRect(for: container)
        return NSSize(width: width, height: max(1, ceil(used.maxY)))
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        textContainer?.containerSize = NSSize(width: max(1, newSize.width), height: CGFloat.greatestFiniteMagnitude)
        window?.invalidateCursorRects(for: self)
    }

    /// NSLayoutManager returns the *nearest* glyph even over blank space. Test
    /// its actual rectangle before accepting a link, including after wrapping.
    func link(at point: NSPoint) -> (entry: DictionaryEntry, range: NSRange)? {
        guard bounds.contains(point), let layoutManager, let textContainer, let textStorage else { return nil }
        layoutManager.ensureLayout(for: textContainer)
        let location = NSPoint(x: point.x - textContainerOrigin.x, y: point.y - textContainerOrigin.y)
        let glyph = layoutManager.glyphIndex(for: location, in: textContainer)
        guard glyph < layoutManager.numberOfGlyphs,
              layoutManager.boundingRect(forGlyphRange: NSRange(location: glyph, length: 1), in: textContainer).contains(location) else { return nil }
        let index = layoutManager.characterIndexForGlyph(at: glyph)
        guard index < textStorage.length else { return nil }
        guard let url = textStorage.attribute(.link, at: index, effectiveRange: nil) as? URL,
              let target = entries[url.lastPathComponent] else { return nil }
        // Adjacent identical kanji can share one attributed link run. Hover
        // still belongs to the single composed character under the pointer.
        let range = (string as NSString).rangeOfComposedCharacterSequence(at: index)
        return (target, range)
    }

    func updateHover(at point: NSPoint?) {
        let target = point.flatMap { link(at: $0) }
        guard hoveredRange != target?.range else { return }
        if let range = hoveredRange {
            layoutManager?.removeTemporaryAttribute(.foregroundColor, forCharacterRange: range)
        }
        hoveredRange = target?.range
        if let target {
            layoutManager?.addTemporaryAttribute(.foregroundColor, value: NSColor(StudyStyle.linkHover), forCharacterRange: target.range)
            toolTip = "Открыть \(target.entry.text): значение и слова с этим кандзи"
        } else {
            toolTip = nil
        }
        needsDisplay = true
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let area = NSTrackingArea(rect: .zero, options: [.inVisibleRect, .activeInKeyWindow, .mouseMoved, .mouseEnteredAndExited, .cursorUpdate], owner: self)
        addTrackingArea(area)
        tracking = area
    }

    override func mouseEntered(with event: NSEvent) { mouseMoved(with: event) }
    override func mouseMoved(with event: NSEvent) {
        super.mouseMoved(with: event)
        let point = convert(event.locationInWindow, from: nil)
        updateHover(at: point)
        (link(at: point) == nil ? NSCursor.iBeam : NSCursor.pointingHand).set()
    }
    override func mouseExited(with event: NSEvent) {
        super.mouseExited(with: event)
        updateHover(at: nil)
    }
    override func cursorUpdate(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        (link(at: point) == nil ? NSCursor.iBeam : NSCursor.pointingHand).set()
    }

    func textView(_ textView: NSTextView, clickedOnLink link: Any, at charIndex: Int) -> Bool {
        guard let url = link as? URL, let target = entries[url.lastPathComponent], let textStorage,
              charIndex >= 0, charIndex < textStorage.length else { return true }
        clearFlash()
        let hovered = hoveredRange.flatMap { range -> NSRange? in
            let hoveredURL = textStorage.attribute(.link, at: range.location, effectiveRange: nil) as? URL
            return hoveredURL == url ? range : nil
        }
        let range = hovered ?? (string as NSString).rangeOfComposedCharacterSequence(at: charIndex)
        flashRange = range
        layoutManager?.addTemporaryAttribute(.backgroundColor, value: NSColor(StudyStyle.accent.opacity(0.18)), forCharacterRange: range)
        flashTask = Task { @MainActor [weak self] in
            do { try await Task.sleep(for: .milliseconds(250)) }
            catch { return }
            self?.clearFlash()
        }
        openKanji(target)
        return true
    }

    private func clearFlash() {
        flashTask?.cancel()
        flashTask = nil
        if let range = flashRange {
            layoutManager?.removeTemporaryAttribute(.backgroundColor, forCharacterRange: range)
        }
        flashRange = nil
    }

    override func viewWillMove(toWindow newWindow: NSWindow?) {
        if newWindow == nil {
            updateHover(at: nil)
            clearFlash()
        }
        super.viewWillMove(toWindow: newWindow)
    }
}
