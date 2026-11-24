import SwiftUI

struct StudySettingsView: View {
    @AppStorage("dailyNewLimit") private var dailyNewLimit = 20
    @AppStorage("readingStyle") private var readingStyle: ReadingStyle = .hiragana
    @AppStorage("refreshInterval") private var refreshInterval: RefreshInterval = .hourly
    @ObservedObject private var hotKey = DictionaryHotKey.shared
    @State private var hotKeyDraft = DictionaryKeyCombination.standard
    @State private var showChangelog = false
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
            Section("Глобальное сочетание для поиска") {
                Text("Открывает поиск по словарю из другого приложения, пока Kotoba запущена.")
                HStack {
                    ForEach(DictionaryKeyCombination.modifierKeys, id: \.1) { title, flag in
                        Toggle(title, isOn: Binding {
                            hotKeyDraft.modifiers & flag != 0
                        } set: { enabled in
                            let modifiers = enabled ? hotKeyDraft.modifiers | flag : hotKeyDraft.modifiers & ~flag
                            hotKeyDraft = DictionaryKeyCombination(key: hotKeyDraft.key, modifiers: modifiers)
                        })
                    }
                }
                Picker("Клавиша", selection: Binding {
                    hotKeyDraft.key
                } set: { key in
                    hotKeyDraft = DictionaryKeyCombination(key: key, modifiers: hotKeyDraft.modifiers)
                }) {
                    ForEach(DictionaryKeyCombination.keys, id: \.1) { title, key in Text(title).tag(key) }
                }
                HStack {
                    Button("Применить") { hotKey.apply(hotKeyDraft) }
                        .disabled(!hotKeyDraft.isValid)
                    Button("По умолчанию") {
                        hotKeyDraft = .standard
                        hotKey.apply(.standard)
                    }
                    Button("Отключить") { hotKey.apply(nil) }
                        .disabled(hotKey.selection == nil)
                }
                if let error = hotKey.registrationError {
                    Text(error).foregroundStyle(StudyStyle.accent)
                }
                if let selection = hotKey.selection {
                    Text("Текущее сочетание: \(selection.title)").foregroundStyle(StudyStyle.muted)
                } else {
                    Text("Сочетание отключено.").foregroundStyle(StudyStyle.muted)
                }
            }
            Section("Чтение хираганой") {
                Text("Чтения слов сверены с их написанием и JMdict. Знак ー сохраняется в словарном написании и там, где точная кана неизвестна. Выше можно выбрать исходное ромадзи.")
                    .font(.callout)
                    .foregroundStyle(StudyStyle.muted)
                Link("JMdict · Electronic Dictionary Research and Development Group · CC BY-SA 4.0",
                     destination: URL(string: "https://www.edrdg.org/edrdg/licence.html")!)
                    .font(.caption)
            }
            Section("Виджеты рабочего стола") {
                WidgetAccessView()
                Text("Нажмите на виджет правой кнопкой → «Изменить Kotoba». Задайте размер слова / кандзи, чтения и перевода (50–150%), формат чтения и таймер. У каждого виджета свои настройки.")
                Text("В виджетах доступны те же интервалы. Время обновления определяет macOS, поэтому возможны задержки.")
                    .font(.caption)
                    .foregroundStyle(StudyStyle.muted)
            }
            if let url = Bundle.main.url(forResource: "Kotoba-Changelog", withExtension: "txt"),
               let changelog = try? String(contentsOf: url, encoding: .utf8) {
                Section("О Kotoba") {
                    Button("Журнал изменений") { showChangelog = true }
                        .sheet(isPresented: $showChangelog) {
                            VStack(alignment: .leading, spacing: 16) {
                                ScrollView {
                                    Text(changelog)
                                        .textSelection(.enabled)
                                        .frame(maxWidth: .infinity, alignment: .leading)
                                }
                                Button("Готово") { showChangelog = false }
                                    .keyboardShortcut(.defaultAction)
                            }
                            .padding(24)
                            .frame(width: 520, height: 420)
                        }
                }
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
        .tint(StudyStyle.accent)
        .background(StudyStyle.paper)
        .onAppear { hotKeyDraft = hotKey.selection ?? .standard }
        .onChange(of: hotKey.selection) { _, selection in hotKeyDraft = selection ?? .standard }
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
