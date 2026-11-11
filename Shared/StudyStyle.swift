import SwiftUI

enum StudyStyle {
    static let paper = Color(red: 0.97, green: 0.95, blue: 0.90)
    static let ink = Color(red: 0.16, green: 0.20, blue: 0.18)
    static let accent = Color(red: 0.71, green: 0.24, blue: 0.15)
    static let linkHover = Color(red: 0.36, green: 0.43, blue: 0.46)
    static let muted = Color(red: 0.40, green: 0.43, blue: 0.38)

    static func japanese(_ size: CGFloat) -> Font {
        .custom("Hiragino Mincho ProN", size: size)
    }
}
