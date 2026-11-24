using Avalonia.Controls;
using Avalonia.Threading;
using Kotoba.Core;
using static Kotoba.Linux.Ui;

namespace Kotoba.Linux;

internal sealed partial class MainWindow
{
    private static readonly (string Value, string Label)[] CardSizes =
        [("Small", "Малый"), ("Medium", "Средний"), ("Large", "Большой")];
    private static readonly (string Value, string Label)[] CardFeeds =
        [("Dictionary", "Словарь"), ("Learning list", "Список обучения")];
    private DesktopCardStore? cards;
    private DesktopCardStore CardStore => cards ??= new(paths.Card, Dictionary, () => Learning);
    internal DesktopCardWindow? CardWindow { get; private set; }

    private Control CardSettingsSection()
    {
        var current = CardStore.Cards.SingleOrDefault();
        var sizes = Row();
        foreach (var size in CardSizes)
            sizes.Children.Add(ActionButton(size.Label, () => ShowCard(CardStore.Create(size.Value)),
                                            current?.Size == size.Value));
        if (current is not null)
        {
            sizes.Children.Add(ActionButton("Настроить карточку", () => EditCard(current.Id)));
            sizes.Children.Add(ActionButton("Убрать карточку", () =>
            {
                CloseCardWindow();
                CardStore.Remove(current.Id);
                Draw();
            }));
        }
        return Card(Column(Text("Настольная карточка", 21), sizes));
    }

    private void RestoreCard()
    {
        if (CardStore.Cards.SingleOrDefault() is { } current)
            ShowCard(current);
    }

    private void ShowCard(DesktopCard card)
    {
        if (CardWindow is null)
        {
            var window = new DesktopCardWindow(this, CardStore, card);
            CardWindow = window;
            window.Closed += (_, _) =>
            {
                if (CardWindow == window)
                    CardWindow = null;
                if (!quitting && !IsVisible)
                    Dispatcher.UIThread.Post(() => { if (!quitting && !IsVisible && CardWindow is null) Quit(); });
            };
            window.Show();
        }
        else
        {
            CardWindow.Refresh();
            CardWindow.Activate();
        }
        if (page == Page.Settings)
            Draw();
    }

    private void CloseCardWindow()
    {
        CardWindow?.Close();
        CardWindow = null;
    }

    internal void OpenCardEntry(DictionaryEntry item)
    {
        BringForward();
        ShowEntry(item);
    }

    internal void EditCard(string id, Window? owner = null)
    {
        var card = CardStore.Get(id);
        var size = Choices(CardSizes, card.Size);
        var feed = Choices(CardFeeds, card.Feed);
        var content = Choices(Contents, card.Content);
        var options = new List<(string, string)> { ("inbox", "Входящие") };
        options.AddRange(Learning.Lists().Select(list => (list.Id, list.Name)));
        var list = Choices(options, options.Any(item => item.Item1 == card.ListId) ? card.ListId : "inbox");
        list.IsEnabled = card.Feed == "Learning list";
        feed.SelectionChanged += (_, _) => list.IsEnabled = Value(feed, card.Feed) == "Learning list";
        var interval = Choices(Intervals, card.RefreshSeconds);
        var romaji = new CheckBox { Content = "Ромадзи", IsChecked = card.Romaji };
        var reading = new CheckBox { Content = "Показывать чтение", IsChecked = card.ShowReading };
        var translation = new CheckBox { Content = "Показывать перевод", IsChecked = card.ShowTranslation };
        var topmost = new CheckBox { Content = "Поверх остальных окон", IsChecked = card.Topmost };
        var word = Number(card.HeadwordPercent, 50, 150);
        var kana = Number(card.ReadingPercent, 50, 150);
        var meaning = Number(card.TranslationPercent, 50, 150);
        var fields = Column(
            Row(Column(Text("Размер"), size), Column(Text("Тип записей"), content)),
            Row(Column(Text("Источник"), feed), Column(Text("Список"), list)),
            Text("Обновление"), interval, reading, translation, romaji, topmost,
            Text("Размер слова, чтения и перевода, %"), Row(word, kana, meaning));
        var scroll = Scroll(fields);
        scroll.MaxHeight = 510;
        DialogContent(Dialog("Настройка карточки"), scroll, "Применить", () =>
        {
            var updated = CardStore.Update(card with
            {
                Size = Value(size, card.Size), Feed = Value(feed, card.Feed), Content = Value(content, card.Content),
                ListId = Value(list, "inbox"), RefreshSeconds = Value(interval, card.RefreshSeconds),
                Romaji = romaji.IsChecked == true, ShowReading = reading.IsChecked == true,
                ShowTranslation = translation.IsChecked == true, Topmost = topmost.IsChecked == true,
                HeadwordPercent = (int)(word.Value ?? card.HeadwordPercent),
                ReadingPercent = (int)(kana.Value ?? card.ReadingPercent),
                TranslationPercent = (int)(meaning.Value ?? card.TranslationPercent)
            });
            if (CardWindow is null)
                ShowCard(updated);
            else
                CardWindow.Refresh();
        }, owner);
    }
}
