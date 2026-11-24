using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kotoba.Core;
using static Kotoba.Linux.Ui;

namespace Kotoba.Linux;

internal sealed partial class MainWindow : Window
{
    private enum Page { Today, Lists, Dictionary, Settings }
    private enum DictionaryView { Search, Explore, Entry }

    private static readonly (Page Page, string Label)[] Pages =
    [
        (Page.Today, "Сегодня"), (Page.Lists, "Мои списки"),
        (Page.Dictionary, "Словарь"), (Page.Settings, "Настройки")
    ];
    private static readonly (string Value, string Label)[] Contents =
        [("Mixed", "Все записи"), ("Kanji", "Кандзи"), ("Words", "Слова")];
    private static readonly (int Value, string Label)[] Intervals =
        [(0, "Вручную"), (300, "5 минут"), (900, "15 минут"), (1800, "30 минут"),
         (3600, "1 час"), (21600, "6 часов"), (86400, "1 день")];

    private readonly UserPaths paths;
    internal readonly DictionaryStore Dictionary = new(Path.Combine(AppContext.BaseDirectory, "Data", "dictionary.sqlite"));
    private LearningStore? learning;
    internal LearningStore Learning => learning ??= new(paths.Learning, Dictionary);
    private Settings settings;
    private readonly Dictionary<Page, Button> navigation = [];
    private readonly TextBlock heading = Text("", 32);
    private readonly TextBlock feedback = Text("", 13, Accent);
    private readonly ContentControl body = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stack<(DictionaryView View, DictionaryEntry? Entry)> history = new();
    private DateTimeOffset nextExplore;
    private Page page = Page.Today;
    private Page? returnPage;
    private DictionaryView dictionaryView;
    private DictionaryEntry? entry;
    private string query = "";
    private string? settingsError;
    private CancellationTokenSource? queryRequest;
    private bool quitting;
    internal TextBox? SearchInput { get; private set; }
    internal ListBox? SearchResults { get; private set; }

    public MainWindow(UserPaths paths)
    {
        this.paths = paths;
        try { settings = Settings.Load(paths.Settings); }
        catch (Exception error)
        {
            settings = new();
            settingsError = $"Не удалось прочитать настройки. {error.Message}";
        }
        Title = "Kotoba";
        Width = 970;
        Height = 710;
        MinWidth = 720;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var mark = Text("字", 42, Accent);
        mark.FontFamily = Japanese;
        var brand = Text("Kotoba", 24);
        brand.FontWeight = FontWeight.SemiBold;
        var sidebar = new DockPanel { Margin = new Thickness(18, 26) };
        var identity = Column(mark, brand, Text("Японско-русский словарь", 12, Muted));
        identity.Margin = new Thickness(8, 0, 0, 26);
        DockPanel.SetDock(identity, Dock.Top);
        sidebar.Children.Add(identity);
        var totals = Text("Словарь работает офлайн", 12, Muted);
        try
        {
            var counts = Dictionary.Counts();
            totals.Text = $"{counts.Kanji:N0} кандзи\n{counts.Words:N0} слов";
        }
        catch (Exception error) { Report(error); }
        totals.Margin = new Thickness(8, 20, 0, 0);
        DockPanel.SetDock(totals, Dock.Bottom);
        sidebar.Children.Add(totals);
        var links = new StackPanel { Spacing = 5 };
        foreach (var item in Pages)
        {
            var button = Button(item.Label, () => Navigate(item.Page));
            button.Classes.Add("nav");
            navigation[item.Page] = button;
            links.Children.Add(button);
        }
        sidebar.Children.Add(links);
        body.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        body.VerticalContentAlignment = VerticalAlignment.Stretch;
        var workspace = new DockPanel { Margin = new Thickness(28, 26) };
        heading.Margin = new Thickness(0, 0, 0, 18);
        DockPanel.SetDock(heading, Dock.Top);
        workspace.Children.Add(heading);
        feedback.IsVisible = false;
        feedback.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(feedback, Dock.Top);
        workspace.Children.Add(feedback);
        workspace.Children.Add(body);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("198,*") };
        layout.Children.Add(new Border
        {
            Background = Brush.Parse("#F0E9DC"), BorderBrush = Brush.Parse("#E1D8CA"),
            BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar
        });
        Grid.SetColumn(workspace, 1);
        layout.Children.Add(workspace);
        Content = layout;
        AddHandler(KeyDownEvent, HandleKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Activated += (_, _) => ResetExplore();
        Closing += (_, args) =>
        {
            if (!quitting && args.CloseReason == WindowCloseReason.WindowClosing && CardWindow?.IsVisible == true)
            {
                args.Cancel = true;
                Hide();
            }
        };
        Closed += (_, _) =>
        {
            quitting = true;
            queryRequest?.Cancel();
            clock.Stop();
            shortcut?.Dispose();
            CloseCardWindow();
        };
        clock.Tick += (_, _) => TickExplore();
        clock.Start();
        Navigate(Page.Today);
        if (settingsError is not null)
            SetFeedback(settingsError);
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs args)
    {
        Opened -= OnOpened;
        StartShortcut();
        Perform(RestoreCard);
    }

    internal void Route(string command)
    {
        BringForward();
        Perform(() =>
        {
            if (command == "--search")
                OpenSearch();
            else if (Uri.TryCreate(command, UriKind.Absolute, out var uri) && uri.Scheme == "yarxi-study")
            {
                switch (uri.Host)
                {
                    case "entry":
                        if (Dictionary.Entry(Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'))) is { } selected)
                            ShowEntry(selected);
                        break;
                    case "lists": Navigate(Page.Lists); break;
                    case "setup": Navigate(Page.Settings); break;
                }
            }
        });
    }

    private void BringForward()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        if (!IsVisible)
            Show();
        Activate();
    }

    private string? Kind => settings.Content switch { "Kanji" => "kanji", "Words" => "word", _ => null };

    private void Navigate(Page next)
    {
        page = next;
        if (next == Page.Dictionary)
        {
            dictionaryView = DictionaryView.Search;
            history.Clear();
            returnPage = null;
            entry = null;
        }
        Draw();
    }

    internal void OpenSearch()
    {
        Navigate(Page.Dictionary);
        Dispatcher.UIThread.Post(() =>
        {
            SearchInput?.Focus();
            SearchInput?.SelectAll();
        }, DispatcherPriority.Input);
    }

    internal void Quit()
    {
        quitting = true;
        Close();
    }

    private void Draw()
    {
        queryRequest?.Cancel();
        SearchInput = null;
        SearchResults = null;
        SetFeedback("");
        heading.Text = Pages.First(item => item.Page == page).Label;
        foreach (var (kind, button) in navigation)
            button.Classes.Set("active", page == kind);
        try
        {
            body.Content = page switch
            {
                Page.Today => TodayPage(),
                Page.Lists => ListsPage(),
                Page.Dictionary => DictionaryPage(),
                _ => SettingsPage()
            };
        }
        catch (Exception error)
        {
            Report(error);
            body.Content = Card(Column(Text("Раздел временно недоступен"), Button("Повторить", Draw)));
        }
    }

    internal void Perform(Action action)
    {
        try { action(); }
        catch (Exception error) { Report(error); }
    }

    internal void Report(Exception error) => SetFeedback($"Не удалось выполнить действие. {error.Message}");

    private void SetFeedback(string text)
    {
        feedback.Text = text;
        feedback.IsVisible = text.Length > 0;
    }

    private Button ActionButton(string label, Action action, bool primary = false) =>
        Button(label, () => Perform(action), primary);

    private Control DictionaryPage()
    {
        var filter = Choices(Contents, settings.Content);
        filter.SelectionChanged += (_, _) => Perform(() =>
        {
            var updated = Value(filter, settings.Content);
            if (updated == settings.Content)
                return;
            SaveSettings(value => value.Content = updated);
            if (dictionaryView == DictionaryView.Explore)
                NextExplore();
            else if (dictionaryView == DictionaryView.Search)
                Draw();
        });
        var header = Row(Button("Поиск", OpenSearch, dictionaryView == DictionaryView.Search),
                         ActionButton("Обзор", NextExplore, dictionaryView == DictionaryView.Explore), filter);
        var panel = new DockPanel();
        header.Margin = new Thickness(0, 0, 0, 14);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(dictionaryView switch
        {
            DictionaryView.Entry => entry is { } selected ? EntryPage(selected) : Text("Запись недоступна"),
            DictionaryView.Explore => entry is { } random ? ExplorePage(random) : Text("Записи не найдены"),
            _ => SearchPage()
        });
        return panel;
    }

    private Control SearchPage()
    {
        var input = new TextBox { Text = query, Watermark = "Поиск по-японски, хираганой, ромадзи или по-русски" };
        var results = EntryList([], item => ShowEntry(item));
        var info = Text("", 13, Muted);
        var header = Column(input, info);
        header.Margin = new Thickness(0, 0, 0, 10);
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(results);
        SearchInput = input;
        SearchResults = results;
        input.TextChanged += (_, _) =>
        {
            if (SearchInput != input)
                return;
            query = input.Text ?? "";
            _ = RefreshQuery(results, info, true);
        };
        _ = RefreshQuery(results, info, false);
        return panel;
    }

    private async Task RefreshQuery(ListBox results, TextBlock info, bool debounce)
    {
        queryRequest?.Cancel();
        using var request = new CancellationTokenSource();
        queryRequest = request;
        string text = query;
        string? kind = Kind;
        results.ItemsSource = Array.Empty<DictionaryEntry>();
        info.Text = "Поиск…";
        try
        {
            if (debounce)
                await Task.Delay(120, request.Token);
            var matches = await Task.Run(() => Dictionary.Search(text, kind), request.Token);
            request.Token.ThrowIfCancellationRequested();
            results.ItemsSource = matches;
            info.Text = matches.Count switch
            {
                0 => "Ничего не найдено", 100 => "Показаны первые 100 результатов",
                _ => $"Найдено записей: {matches.Count}"
            };
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { if (!request.IsCancellationRequested) Report(error); }
        finally { if (queryRequest == request) queryRequest = null; }
    }

    private ListBox EntryList(IEnumerable<DictionaryEntry> entries, Action<DictionaryEntry> open)
    {
        var children = new ListBox { ItemsSource = entries };
        children.ItemTemplate = new FuncDataTemplate<DictionaryEntry>((item, _) => EntryRow(item));
        children.SelectionChanged += (_, _) =>
        {
            if (children.SelectedItem is DictionaryEntry item)
                Perform(() => open(item));
        };
        return children;
    }

    private Control EntryRow(DictionaryEntry item)
    {
        var word = Text(item.Text, 23 * settings.HeadwordScale, Accent);
        word.FontFamily = Japanese;
        var label = Text(item.Read(settings.Romaji), 13 * settings.ReadingScale);
        label.FontFamily = Japanese;
        var details = Column(word, label, Text(item.Meaning, 14 * settings.TranslationScale, Muted));
        details.Spacing = 3;
        details.Margin = new Thickness(8, 4);
        return details;
    }

    internal void ShowEntry(DictionaryEntry item, bool nested = false)
    {
        if (nested && page == Page.Dictionary)
            history.Push((dictionaryView, entry));
        else
        {
            history.Clear();
            returnPage = page == Page.Dictionary ? null : page;
        }
        page = Page.Dictionary;
        dictionaryView = DictionaryView.Entry;
        entry = item;
        Draw();
    }

    private void Back()
    {
        if (history.TryPop(out var previous))
        {
            (dictionaryView, entry) = previous;
            Draw();
        }
        else if (returnPage is { } origin)
        {
            returnPage = null;
            Navigate(origin);
        }
        else
            OpenSearch();
    }

    private Control EntryPage(DictionaryEntry item) =>
        EntryWithRelatedWords(item, Column(Button("Назад", Back), EntryContent(item, false), EntryActions(item)));

    private Control ExplorePage(DictionaryEntry item) =>
        EntryWithRelatedWords(item, Column(EntryContent(item, false), EntryActions(item),
                                          ActionButton("Следующая запись", NextExplore, true)));

    private Control EntryContent(DictionaryEntry item, bool review, bool showMeaning = true)
    {
        var kanji = item.IsKanji ? [] : Dictionary.KanjiIn(item.Text);
        double size = (item.IsKanji ? 64 : 42) * settings.HeadwordScale;
        Control headword = kanji.Count == 0 ? Selectable(item.Text, size, true) :
            new InteractiveHeadword(item.Text, size, kanji, target => ShowEntry(target, true));
        var content = Column(
            headword,
            Selectable(item.Read(!review && settings.Romaji), 20 * settings.ReadingScale, true));
        if (showMeaning)
        {
            content.Children.Add(Selectable(item.Meaning, 20 * settings.TranslationScale));
            if (item.Detail.Length > 0 && item.Detail != item.Meaning)
                content.Children.Add(Selectable(item.Detail, 16 * settings.TranslationScale));
        }
        return content;
    }

    private Control EntryActions(DictionaryEntry item)
    {
        var copy = new Button { Content = "Копировать" };
        int copyRequest = 0;
        copy.Click += async (_, _) =>
        {
            int attempt = ++copyRequest;
            string message;
            try
            {
                var clipboard = Clipboard ?? throw new InvalidOperationException("Буфер обмена недоступен");
                await clipboard.SetTextAsync(item.Text);
                message = "Скопировано ✓";
            }
            catch (Exception) { message = "Не удалось скопировать"; }
            if (copyRequest != attempt)
                return;
            copy.Content = message;
            await Task.Delay(2000);
            if (copyRequest == attempt)
                copy.Content = "Копировать";
        };
        var save = ActionButton("Во входящие", () => { Learning.Save(item.Id); SetFeedback("Запись сохранена"); });
        return Row(copy, save, ActionButton("По спискам", () => Organize(item)));
    }

    private Control EntryWithRelatedWords(DictionaryEntry item, StackPanel details)
    {
        if (!item.IsKanji)
            return Scroll(Card(details));
        var words = Dictionary.WordsContainingKanji(item.Text);
        details.Children.Add(Text($"Слова с {item.Text} · {words.Count}", 20));
        var rows = new ItemsControl
        {
            ItemsSource = words.Cast<object>().Prepend(Card(details)).ToArray(),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<DictionaryEntry>((word, _) =>
            {
                var row = ActionButton("", () => ShowEntry(word, true));
                row.Content = EntryRow(word);
                row.HorizontalAlignment = HorizontalAlignment.Stretch;
                return row;
            })
        };
        return Scroll(rows);
    }

    private void NextExplore()
    {
        var next = Dictionary.Random(Kind, dictionaryView == DictionaryView.Explore ? entry?.Id : null);
        entry = next ?? Dictionary.Random(Kind, null);
        dictionaryView = DictionaryView.Explore;
        history.Clear();
        returnPage = null;
        ResetExplore();
        Draw();
    }

    private void ResetExplore() => nextExplore = DateTimeOffset.UtcNow.AddSeconds(settings.RefreshSeconds);

    private void TickExplore()
    {
        if (IsActive && page == Page.Dictionary && dictionaryView == DictionaryView.Explore &&
            settings.RefreshSeconds > 0 && DateTimeOffset.UtcNow >= nextExplore)
        {
            ResetExplore();
            Perform(NextExplore);
        }
    }

    private void HandleKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            OpenSearch();
            args.Handled = true;
        }
        else if (args.Source is not TextBox && args.KeyModifiers == KeyModifiers.None &&
                 page == Page.Dictionary && dictionaryView == DictionaryView.Explore && args.Key == Key.Space &&
                 args.Source is not Avalonia.Controls.Button and not ComboBox)
        {
            Perform(NextExplore);
            args.Handled = true;
        }
        else
            Perform(() => args.Handled = ReviewKey(args));
    }

    private void SaveSettings(Action<Settings> set)
    {
        var updated = System.Text.Json.JsonSerializer.Deserialize<Settings>(
            System.Text.Json.JsonSerializer.Serialize(settings))!;
        set(updated);
        updated.Save(paths.Settings);
        settings = updated;
        settingsError = null;
    }
}
