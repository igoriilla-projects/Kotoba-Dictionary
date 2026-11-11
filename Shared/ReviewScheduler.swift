import Foundation

enum ReviewGrade: Int, CaseIterable, Identifiable {
    case again = 1, good = 3, easy = 4
    var id: Int { rawValue }
    var title: String { switch self { case .again: return "Ещё раз"; case .good: return "Помню"; case .easy: return "Легко" } }
    var rating: Rating { Rating(rawValue: rawValue)! }
}

/// All scheduling goes through this adapter. State is the pinned release's Codable
/// Card, without lossy conversion. A fresh algorithm prevents mutable preview state
/// from leaking between cards or transactions.
struct ReviewScheduler {
    func next(_ card: Card?, grade: ReviewGrade, at date: Date) -> RecordLogItem {
        let algorithm = FSRSAlgorithm(parameters: FSRSParameters(requestRetention: 0.9, enableFuzz: false, enableShortTerm: true))
        return BasicScheduler(card: card ?? Card(due: date), reviewTime: date, algorithm: algorithm).review(grade.rating)
    }
    func interval(_ card: Card?, grade: ReviewGrade, at date: Date) -> String {
        let seconds = max(0, next(card, grade: grade, at: date).card.due.timeIntervalSince(date))
        if seconds < 3600 { return "\(max(1, Int(seconds / 60))) мин" }
        if seconds < 86400 { return "\(Int(seconds / 3600)) ч" }
        return "\(Int((seconds / 86400).rounded())) дн."
    }
}
