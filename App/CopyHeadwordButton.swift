import AppKit
import SwiftUI

struct CopyHeadwordButton: View {
    let text: String
    @State private var feedback = HeadwordCopyFeedback.ready
    @State private var feedbackRevision = 0
    @State private var hovering = false

    var body: some View {
        Button {
            feedback = HeadwordCopyFeedback.copy(text, to: .general)
            feedbackRevision += 1
        } label: {
            HeadwordCopyLabel(feedback: feedback)
        }
        .buttonStyle(HeadwordCopyButtonStyle(hovering: hovering))
        .modifier(LinkPointer())
        .onHover { hovering = $0 }
        .help(feedback == .copied ? "«\(text)» скопировано в буфер обмена" : feedback == .failed ? "Не удалось скопировать. Нажмите ещё раз." : "Скопировать целиком: \(text)")
        .task(id: feedbackRevision) {
            guard feedback != .ready else { return }
            do { try await Task.sleep(for: .seconds(2)) }
            catch { return }
            feedback = .ready
        }
        .onChange(of: text) { _, _ in
            feedback = .ready
            feedbackRevision += 1
        }
    }
}

enum HeadwordCopyFeedback {
    case ready, copied, failed

    var title: String {
        switch self {
        case .ready: return "Копировать"
        case .copied: return "Скопировано"
        case .failed: return "Не скопировано"
        }
    }

    var symbol: String {
        switch self {
        case .ready: return "doc.on.doc"
        case .copied: return "checkmark.circle.fill"
        case .failed: return "exclamationmark.circle"
        }
    }

    static func copy(_ text: String, to pasteboard: NSPasteboard) -> Self {
        pasteboard.clearContents()
        return pasteboard.setString(text, forType: .string) ? .copied : .failed
    }
}

struct HeadwordCopyLabel: View {
    let feedback: HeadwordCopyFeedback

    var body: some View {
        ZStack(alignment: .leading) {
            // Reserve the original label width so confirmation never moves
            // neighboring controls or the button out from under the pointer.
            Label("Копировать", systemImage: "doc.on.doc").hidden()
            Label("Не скопировано", systemImage: "exclamationmark.circle").hidden()
            Label(feedback.title, systemImage: feedback.symbol)
        }
        .foregroundStyle(feedback == .ready ? StudyStyle.ink : StudyStyle.accent)
    }
}

private struct HeadwordCopyButtonStyle: ButtonStyle {
    let hovering: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .padding(.horizontal, 8).padding(.vertical, 5)
            .background(StudyStyle.accent.opacity(isEnabled && configuration.isPressed ? 0.18 : isEnabled && hovering ? 0.08 : 0), in: RoundedRectangle(cornerRadius: 6))
            .contentShape(RoundedRectangle(cornerRadius: 6))
            .opacity(isEnabled ? 1 : 0.5)
            .scaleEffect(!reduceMotion && configuration.isPressed ? 0.97 : 1)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: configuration.isPressed)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: hovering)
    }
}

/// Use native pointer handling on current macOS, and balance the cursor stack
/// on macOS 14 even if a hovered view disappears during navigation.
struct LinkPointer: ViewModifier {
    var enabled = true
    @Environment(\.isEnabled) private var isEnabled
    @State private var cursorPushed = false
    @State private var hovering = false

    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.pointerStyle(enabled && isEnabled ? .link : nil)
        } else {
            content
                .onHover { active in
                    hovering = active
                    updateCursor(active && enabled && isEnabled)
                }
                .onChange(of: enabled && isEnabled) { _, enabled in
                    updateCursor(enabled && hovering)
                }
                .onDisappear { updateCursor(false) }
        }
    }

    private func updateCursor(_ active: Bool) {
        if active && !cursorPushed {
            NSCursor.pointingHand.push()
            cursorPushed = true
        } else if !active && cursorPushed {
            NSCursor.pop()
            cursorPushed = false
        }
    }
}
