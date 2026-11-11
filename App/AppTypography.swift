import SwiftUI

/// App-only preferences preserve each view's default proportions.
struct AppTypography: DynamicProperty {
    @AppStorage("appHeadwordScale") var headwordScale = 1.0
    @AppStorage("appReadingScale") var readingScale = 1.0
    @AppStorage("appTranslationScale") var translationScale = 1.0

    static let range = 0.5...2.0

    func headword(_ size: CGFloat) -> Font {
        StudyStyle.japanese(headwordSize(size))
    }

    func headwordSize(_ size: CGFloat) -> CGFloat { size * resolved(headwordScale) }

    func reading(_ size: CGFloat, design: Font.Design = .default) -> Font {
        .system(size: size * resolved(readingScale), design: design)
    }

    func translation(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .system(size: size * resolved(translationScale), weight: weight)
    }

    func reset() {
        headwordScale = 1
        readingScale = 1
        translationScale = 1
    }

    private func resolved(_ scale: Double) -> CGFloat {
        CGFloat(scale.isFinite ? min(max(scale, Self.range.lowerBound), Self.range.upperBound) : 1)
    }
}
