using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Kotoba.Core;
using static Kotoba.Linux.Ui;

namespace Kotoba.Linux;

internal sealed partial class MainWindow
{
    private bool reviewing;
    private string? listScope;

    private Control TodayPage()
    {
        var store = Learning;
        var session = store.Session();
        if (reviewing)
            return ReviewPage(store, session);

        var summary = store.Summary(settings.DailyNewLimit);
        var statistics = Row(
            Card(Column(Text("К повторению", 14, Muted), Text(summary.Due.ToString(), 26))),
            Card(Column(Text("Лимит новых на сегодня", 14, Muted), Text(summary.RemainingNew.ToString(), 26))),
            Card(Column(Text("Новые в списках", 14, Muted), Text(summary.AvailableNew.ToString(), 26))));
        var label = session?.CurrentId is not null ? "Продолжить повторение" : "Начать повторение";
        return Scroll(Column(statistics, ActionButton(label, () => StartReview(resume: session?.CurrentId is not null), true)));
    }

    private Control ReviewPage(LearningStore store, ReviewSession? session)
    {
        var header = Row(ActionButton("Прервать", () => { reviewing = false; Draw(); }));
        if (store.CanUndo())
            header.Children.Add(ActionButton("Отменить оценку", () => { store.Undo(); Draw(); }));
        var card = Column(header);
        if (session?.CurrentId is not string id || Dictionary.Entry(id) is not { } item)
        {
            card.Children.Add(Text("На сегодня всё готово", 24));
            card.Children.Add(Text("Добавьте записи в список или вернитесь, когда подойдёт время повторения.", 14, Muted));
            return Scroll(Card(card));
        }

        card.Children.Add(EntryContent(item, review: true, showMeaning: session.RevealedAt is not null));
        if (session.RevealedAt is null)
            card.Children.Add(ActionButton("Показать перевод · Пробел", Reveal, true));
        else
        {
            card.Children.Add(Row(
                ActionButton("1  Ещё раз", () => Rate(ReviewGrade.Again)),
                ActionButton("2  Помню", () => Rate(ReviewGrade.Good), true),
                ActionButton("3  Легко", () => Rate(ReviewGrade.Easy))));
        }
        return Scroll(Card(card));
    }

    private void StartReview(string? scope = null, bool resume = false)
    {
        if (resume)
            Learning.Resume(settings.DailyNewLimit);
        else
            Learning.Start(scope, settings.DailyNewLimit);
        reviewing = true;
        Navigate(Page.Today);
    }

    private void Reveal()
    {
        Learning.Reveal();
        Draw();
    }

    private void Rate(ReviewGrade grade)
    {
        if (Learning.Session()?.CurrentId is not string current)
            return;
        Learning.Rate(current, grade);
        Draw();
    }

    private bool ReviewKey(KeyEventArgs input)
    {
        if (page != Page.Today || !reviewing || input.Source is TextBox or ComboBox)
            return false;
        if (input.KeyModifiers == KeyModifiers.Control && input.Key == Key.Z)
        {
            Perform(() => { if (Learning.CanUndo()) { Learning.Undo(); Draw(); } });
            return true;
        }
        if (input.KeyModifiers != KeyModifiers.None || learning is null ||
            Learning.Session() is not { CurrentId: not null } session)
            return false;
        if (input.Key == Key.Space && session.RevealedAt is null && input.Source is not Avalonia.Controls.Button)
        {
            Perform(Reveal);
            return true;
        }
        ReviewGrade? grade = input.Key switch
        {
            Key.D1 or Key.NumPad1 => ReviewGrade.Again,
            Key.D2 or Key.NumPad2 => ReviewGrade.Good,
            Key.D3 or Key.NumPad3 => ReviewGrade.Easy,
            _ => null
        };
        if (session.RevealedAt is null || grade is null)
            return false;
        Perform(() => Rate(grade.Value));
        return true;
    }

    private Control ListsPage()
    {
        var store = Learning;
        var named = store.Lists();
        if (listScope is not null and not "inbox" && named.All(list => list.Id != listScope))
            listScope = null;
        var choices = new List<(string?, string)> { (null, "Все сохранённые"), ("inbox", "Входящие") };
        choices.AddRange(named.Select(list => ((string?)list.Id, list.Name)));
        var picker = Choices(choices, listScope);
        picker.SelectionChanged += (_, _) => { listScope = Value<string?>(picker, null); Draw(); };
        var actions = Row(picker, ActionButton("Новый список", () => EditList(null)),
                          ActionButton("Повторить", () => StartReview(listScope), true));
        var currentList = named.FirstOrDefault(list => list.Id == listScope);
        if (currentList is not null)
        {
            actions.Children.Add(ActionButton("Переименовать", () => EditList(currentList)));
            actions.Children.Add(ActionButton("Удалить список", () => Confirm(
                $"Удалить список «{currentList.Name}»?", "Удалить",
                () => { store.DeleteList(currentList.Id); listScope = null; Draw(); })));
        }
        var saved = store.Records(listScope).Select(record => Dictionary.Entry(record.Id))
            .OfType<DictionaryEntry>().ToArray();
        var panel = new DockPanel();
        actions.Margin = new Thickness(0, 0, 0, 14);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        panel.Children.Add(saved.Length > 0
            ? EntryList(saved, item => ShowEntry(item))
            : Card(Text("В этом списке пока нет записей.", 16, Muted)));
        return panel;
    }

    private void EditList(LearningList? list)
    {
        var input = new TextBox { Text = list?.Name ?? "", Watermark = "Название списка" };
        var dialog = Dialog(list is null ? "Новый список" : "Переименовать список");
        dialog.Opened += (_, _) => input.Focus();
        DialogContent(dialog, input, "Сохранить", () =>
        {
            if (list is null)
                listScope = Learning.CreateList(input.Text ?? "");
            else
                Learning.RenameList(list.Id, input.Text ?? "");
            Draw();
        });
    }

    private void Organize(DictionaryEntry item)
    {
        var dialog = Dialog("Списки для " + item.Text);
        var box = Column();
        var store = Learning;
        var record = store.Records().FirstOrDefault(row => row.Id == item.Id);
        foreach (var list in store.Lists())
        {
            var check = new CheckBox { Content = list.Name, IsChecked = record?.Lists.Contains(list.Id) == true };
            bool applying = false;
            void Update()
            {
                if (applying)
                    return;
                bool included = check.IsChecked == true;
                try
                {
                    if (included)
                        store.Save(item.Id);
                    store.Membership(item.Id, list.Id, included);
                }
                catch (Exception error)
                {
                    applying = true;
                    check.IsChecked = !included;
                    applying = false;
                    dialog.Title = $"Не удалось изменить список. {error.Message}";
                }
            }
            check.IsCheckedChanged += (_, _) => Update();
            box.Children.Add(check);
        }
        if (box.Children.Count == 0)
            box.Children.Add(Text("Именованные списки можно создать в разделе «Мои списки».", 14, Muted));
        box.Children.Add(Row(
            ActionButton("Во входящие", () => { store.Save(item.Id); SetFeedback("Запись сохранена"); }),
            ActionButton("Убрать из сохранённых", () => Confirm(
                "Убрать запись из сохранённых?", "Убрать",
                () => { store.Remove(item.Id); dialog.Close(); Draw(); }, dialog))));
        DialogContent(dialog, Scroll(box), "Готово", () => { });
    }

    private static Window Dialog(string title) => new()
    {
        Title = title, Width = 420, SizeToContent = SizeToContent.Height,
        MaxHeight = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false
    };

    private void DialogContent(Window dialog, Control contents, string submit, Action save, Window? owner = null)
    {
        var error = Text("", 13, Accent);
        var surface = Column(contents, error,
            Row(Button(submit, () =>
            {
                try { save(); dialog.Close(); }
                catch (Exception exception) { error.Text = exception.Message; }
            }, true), Button("Отмена", dialog.Close)));
        surface.Margin = new Thickness(20);
        dialog.Content = surface;
        _ = dialog.ShowDialog(owner ?? this);
    }

    private void Confirm(string question, string label, Action action, Window? owner = null) =>
        DialogContent(Dialog(question), Text(question), label, action, owner);

    private Control SettingsPage()
    {
        var style = Choices([(false, "Хирагана"), (true, "Ромадзи")], settings.Romaji);
        var interval = Choices(Intervals, settings.RefreshSeconds);
        var quota = Number(settings.DailyNewLimit, 0, 200);
        var word = Number((int)(settings.HeadwordScale * 100), 50, 200);
        var reading = Number((int)(settings.ReadingScale * 100), 50, 200);
        var translation = Number((int)(settings.TranslationScale * 100), 50, 200);
        var readingSection = Card(Column(Text("Словарь и обучение", 21), Text("Формат чтения"), style,
            Text("Таймер обзора"), interval, Text("Новых карточек в день"), quota,
            Text("Размер слова, чтения и перевода, %"), Row(word, reading, translation),
            ActionButton("Сохранить настройки", () =>
            {
                SaveSettings(updated =>
                {
                    updated.Romaji = Value(style, settings.Romaji);
                    updated.RefreshSeconds = Value(interval, settings.RefreshSeconds);
                    updated.DailyNewLimit = (int)(quota.Value ?? settings.DailyNewLimit);
                    updated.HeadwordScale = (double)(word.Value ?? 100) / 100;
                    updated.ReadingScale = (double)(reading.Value ?? 100) / 100;
                    updated.TranslationScale = (double)(translation.Value ?? 100) / 100;
                });
                SetFeedback("Настройки сохранены");
            }, true)));
        var version = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        var changelog = ActionButton("Журнал изменений", () =>
        {
            var contents = Selectable(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHANGELOG.txt")), 15);
            DialogContent(Dialog("Журнал изменений"), new ScrollViewer { Content = contents, MaxHeight = 460 },
                          "Готово", () => { });
        });
        return Scroll(Column(CardSettingsSection(), readingSection, ShortcutSection(),
            Row(changelog, Button("Выйти из Kotoba", Quit)),
            Text($"Kotoba {version} для Linux · JMdict (EDRDG), CC BY-SA 4.0", 13, Muted)));
    }

    private static NumericUpDown Number(int value, int minimum, int maximum) => new()
    {
        Value = value, Minimum = minimum, Maximum = maximum, FormatString = "0",
        Width = 100, HorizontalAlignment = HorizontalAlignment.Left
    };
}
