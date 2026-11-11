import SwiftUI
import WidgetKit

struct TodayView: View {
    @ObservedObject var model: LearningModel
    @State private var lookupEntry: DictionaryEntry?
    private var typography = AppTypography()
    var body: some View {
        VStack(alignment: .leading, spacing: 24) {
            if let error = model.error {
                Text(error).foregroundStyle(StudyStyle.accent)
                Button("Повторить подключение") { model.refresh() }
            }
            HStack(spacing: 36) {
                metric("\(model.summary.due)", "пора повторить")
                metric("\(model.summary.remainingNew)", "новых на сегодня")
            }
            if model.reviewing {
                review
            } else {
                Text("Сохраняйте слова и кандзи из словаря или виджетов и повторяйте их здесь понемногу каждый день.")
                    .foregroundStyle(StudyStyle.muted)
                Button(model.session?.currentID == nil ? "Начать повторение" : "Продолжить повторение") {
                    model.start(resume: model.session?.currentID != nil)
                }.buttonStyle(.borderedProminent)
                Text("Новых записей в запасе: \(model.summary.availableNew) · сначала идут повторения")
                    .font(.caption).foregroundStyle(StudyStyle.muted)
            }
            Spacer(minLength: 0)
        }
        .sheet(item: $lookupEntry) { kanji in
            KanjiLookupView(entry: kanji, dictionary: model.dictionary, learning: model, readingStyle: .hiragana)
        }
    }
    private func metric(_ number: String, _ label: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(number).font(.system(size: 36, design: .serif))
            Text(label).font(.callout).foregroundStyle(StudyStyle.muted)
        }
    }
    @ViewBuilder private var review: some View {
        if let entry = model.current {
            ScrollView {
                VStack(spacing: 20) {
                    InteractiveHeadword(entry: entry, dictionary: model.dictionary, fontSize: typography.headwordSize(entry.isKanji ? 100 : 62), openKanji: { lookupEntry = $0 })
                        .textSelection(.enabled)
                        .multilineTextAlignment(.center)
                        .fixedSize(horizontal: false, vertical: true)
                    CopyHeadwordButton(text: entry.text)
                    Text(entry.reading(in: .hiragana)).font(typography.reading(15)).foregroundStyle(StudyStyle.accent)
                        .textSelection(.enabled)
                    Divider()
                    if let revealed = model.session?.revealedAt {
                        Text(entry.meaning).font(typography.translation(17)).textSelection(.enabled)
                        HStack(spacing: 14) {
                            ForEach(ReviewGrade.allCases) { grade in
                                Button { model.rate(grade) } label: {
                                    VStack(spacing: 5) {
                                        Text(grade.title)
                                        Text(ReviewScheduler().interval(model.currentCard, grade: grade, at: revealed)).font(.caption)
                                    }.frame(minWidth: 90)
                                }
                                .buttonStyle(.bordered)
                                .keyboardShortcut(key(grade), modifiers: [])
                            }
                        }
                        Text("1 Ещё раз · 2 Помню · 3 Легко").font(.caption).foregroundStyle(StudyStyle.muted)
                    } else {
                        Button("Показать перевод · Пробел") { model.reveal() }
                            .buttonStyle(.borderedProminent).keyboardShortcut(.space, modifiers: [])
                    }
                }.padding(30).frame(maxWidth: .infinity)
            }.background(.white.opacity(0.58), in: RoundedRectangle(cornerRadius: 18))
            HStack {
                Button("Отменить оценку") { model.undo() }.disabled(!model.canUndo).keyboardShortcut("z", modifiers: .command)
                Spacer()
                Button("Прервать повторение") { model.reviewing = false }
            }
        } else {
            Text("На сегодня всё повторено.").font(.title2)
            Text("Возвращайтесь, когда придёт время повторения, или сохраните новые записи для изучения.").foregroundStyle(StudyStyle.muted)
            HStack {
                Button("Проверить повторения") { model.start(resume: true) }
                Button("Отменить оценку") { model.undo() }.disabled(!model.canUndo)
                Button("Готово") { model.reviewing = false }
            }
        }
    }
    private func key(_ grade: ReviewGrade) -> KeyEquivalent {
        switch grade { case .again: return "1"; case .good: return "2"; case .easy: return "3" }
    }
}

struct LearningListsView: View {
    @ObservedObject var model: LearningModel
    let open: (DictionaryEntry) -> Void
    let review: (String?) -> Void
    private var typography = AppTypography()
    @State private var scope = "inbox"
    @State private var query = ""
    @State private var kind: StudyContent = .mixed
    @State private var name = ""
    @State private var editingList: String?
    @State private var showName = false
    private var filtered: [DictionaryEntry] {
        model.saved.filter { record in
            scope == "all" || (scope == "inbox" ? record.lists.isEmpty : record.lists.contains(scope))
        }.compactMap { model.entries[$0.id] }.filter { entry in
            (kind.databaseKind == nil || kind.databaseKind == entry.kind) && (query.isEmpty ||
                [entry.text, entry.hiragana, entry.reading, entry.meaning].contains { $0.localizedStandardContains(query) })
        }
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            if let error = model.error {
                Text(error).font(.callout).foregroundStyle(StudyStyle.accent)
                Button("Повторить") { model.refresh() }
            }
            HStack {
                Picker("Список", selection: $scope) {
                    Text("Входящие").tag("inbox")
                    Text("Все сохранённые").tag("all")
                    ForEach(model.lists) { Text($0.name).tag($0.id) }
                }
                Button("Новый список") { editingList = nil; name = ""; showName = true }
                if scope != "inbox" && scope != "all" {
                    Menu {
                        Button("Переименовать") { editingList = scope; name = model.lists.first { $0.id == scope }?.name ?? ""; showName = true }
                        Button("Удалить список") { model.delete(scope); scope = "inbox" }
                    } label: { Image(systemName: "ellipsis.circle") }
                }
            }
            HStack {
                TextField("Поиск в сохранённом…", text: $query).textFieldStyle(.roundedBorder)
                Picker("Содержимое", selection: $kind) {
                    ForEach(StudyContent.allCases, id: \.self) { Text($0.title).tag($0) }
                }.labelsHidden().frame(width: 120)
            }
            HStack {
                Text("Записей: \(filtered.count)").font(.caption).foregroundStyle(StudyStyle.muted)
                Spacer()
                Button("Повторить список") { review(scope == "all" ? nil : scope) }
            }
            if filtered.isEmpty {
                ContentUnavailableView("Здесь пока ничего нет", systemImage: "tray", description: Text("Сохраняйте записи в словаре или виджете. Меню «По спискам» позволяет распределить их по спискам."))
            } else {
                ScrollView {
                    LazyVStack(spacing: 0) {
                        ForEach(filtered) { entry in
                            HStack(spacing: 16) {
                                Button { open(entry) } label: {
                                    HStack(spacing: 18) {
                                        Text(entry.text).font(typography.headword(26)).frame(width: 110, alignment: .leading)
                                        VStack(alignment: .leading, spacing: 5) {
                                            Text(entry.reading(in: .hiragana)).font(typography.reading(12)).foregroundStyle(StudyStyle.accent)
                                            Text(entry.meaning).font(typography.translation(12)).lineLimit(2)
                                        }
                                        Spacer()
                                    }.contentShape(Rectangle())
                                }.buttonStyle(.plain)
                                .contextMenu {
                                    CopyHeadwordButton(text: entry.text)
                                }
                                EntrySaveControls(model: model, entry: entry)
                            }.padding(12)
                            Divider()
                        }
                    }
                }.background(.white.opacity(0.45), in: RoundedRectangle(cornerRadius: 12))
            }
        }
        .alert(editingList == nil ? "Новый список" : "Переименовать список", isPresented: $showName) {
            TextField("Название списка", text: $name)
            Button("Сохранить") { if let id = editingList { model.rename(id, name: name) } else { model.create(name) } }
            Button("Отмена", role: .cancel) {}
        }
    }
}

struct EntrySaveControls: View {
    @ObservedObject var model: LearningModel
    let entry: DictionaryEntry
    private var record: SavedEntry? { model.saved.first { $0.id == entry.id } }
    var body: some View {
        HStack {
            if let record {
                Label("Сохранено", systemImage: "checkmark").font(.callout)
                Menu("По спискам") {
                    if model.lists.isEmpty { Text("Создайте список в разделе «Мои списки»") }
                    ForEach(model.lists) { list in
                        Toggle(list.name, isOn: Binding(get: { record.lists.contains(list.id) }, set: { model.setMembership(entry.id, list: list.id, included: $0) }))
                    }
                    Divider()
                    Button("Удалить из сохранённого") { model.remove(entry.id) }
                }
            } else {
                Button { model.save(entry.id) } label: { Label("Во входящие", systemImage: "bookmark") }
            }
        }
    }
}

struct WidgetAccessView: View {
    @State private var status = "Проверьте, что приложению и виджету доступны общие списки обучения."
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Настройка виджетов").font(.headline)
            Text("Храните Kotoba в постоянной папке. Добавьте его через меню «Изменить виджеты» на рабочем столе. В настройках каждого виджета можно выбрать словарь или список обучения, размеры текста, формат чтения и таймер.")
            Text("Разрешите доступ к общим данным, если macOS запросит его. Затем нажмите «Повторить» в приложении и виджете. Галочка сохранения появится только после записи в базу.")
            Text(status).font(.caption).textSelection(.enabled)
            HStack {
                Button("Проверить доступ") {
                    do { status = try LearningContainer.probe(role: "app") } catch { status = error.localizedDescription }
                    WidgetCenter.shared.reloadAllTimelines()
                }
                Button("Обновить виджеты") { WidgetCenter.shared.reloadAllTimelines() }
            }
            Text("После обновления запустите tools/restart_widget.command и нажмите «Обновить виджеты».").font(.caption).foregroundStyle(StudyStyle.muted)
        }
    }
}
