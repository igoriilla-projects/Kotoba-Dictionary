import SwiftUI

struct StudySettingsView: View {
    @AppStorage("dailyNewLimit") private var dailyNewLimit = 20
    @AppStorage("readingStyle") private var readingStyle: ReadingStyle = .hiragana
    @AppStorage("refreshInterval") private var refreshInterval: RefreshInterval = .hourly
    private var typography = AppTypography()

    var body: some View {
        Form {
            Section("Размер текста") {
                sizeControl("Слово / кандзи", scale: typography.$headwordScale)
                sizeControl("Чтение", scale: typography.$readingScale)
                sizeControl("Перевод", scale: typography.$translationScale)
                VStack(alignment: .leading, spacing: 8) {
                    Text("日本語").font(typography.headword(30))
                    Text("にほんご").font(typography.reading(16)).foregroundStyle(StudyStyle.accent)
                    Text("японский язык").font(typography.translation(16))
                }
                .fixedSize(horizontal: false, vertical: true)
                .accessibilityLabel("Пример размера текста")
                Button("Сбросить размеры") { typography.reset() }
            }
            Section("Обучение") {
                Stepper("Новых записей в день: \(dailyNewLimit)", value: $dailyNewLimit, in: 0...200)
                Text("Лимит общий для всех списков; повторения в него не входят. Новая запись учитывается при первой оценке, а не при сохранении или просмотре. На лицевой стороне карточки чтение всегда дано хираганой.").font(.caption).foregroundStyle(StudyStyle.muted)
            }
            Section("Обзор") {
                Picker("Формат чтения", selection: $readingStyle) {
                    ForEach(ReadingStyle.allCases, id: \.self) { style in
                        Text(style.title).tag(style)
                    }
                }
                Picker("Таймер обновления", selection: $refreshInterval) {
                    ForEach(RefreshInterval.allCases, id: \.self) { interval in
                        Text(interval.title).tag(interval)
                    }
                }
                Text("Показывать новую случайную запись, пока открыт «Словарь → Обзор» и приложение активно. В режиме «Вручную» запись меняется только по кнопке «Дальше».")
                    .font(.caption)
                    .foregroundStyle(StudyStyle.muted)
            }
            Section("Чтение хираганой") {
                Text("Чтение переведено в хирагану из словарного ромадзи. Долгие гласные обозначаются знаком ー, если источник не уточняет их написание каной. Выше можно выбрать исходное ромадзи.")
                    .font(.callout)
                    .foregroundStyle(StudyStyle.muted)
            }
            Section("Виджеты рабочего стола") {
                WidgetAccessView()
                Text("Нажмите на виджет правой кнопкой → «Изменить Kotoba». Задайте размер слова / кандзи, чтения и перевода (50–150%), формат чтения и таймер. У каждого виджета свои настройки.")
                Text("В виджетах доступны те же интервалы. Время обновления определяет macOS, поэтому возможны задержки.")
                    .font(.caption)
                    .foregroundStyle(StudyStyle.muted)
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
        .tint(StudyStyle.accent)
        .background(StudyStyle.paper)
    }

    private func sizeControl(_ title: String, scale: Binding<Double>) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title)
                Spacer()
                Text(scale.wrappedValue.formatted(.percent.precision(.fractionLength(0)).locale(Locale(identifier: "ru_RU"))))
                    .monospacedDigit()
                    .foregroundStyle(StudyStyle.muted)
            }
            Slider(value: scale, in: AppTypography.range, step: 0.05) {
                Text(title)
            }
            .labelsHidden()
        }
    }
}
