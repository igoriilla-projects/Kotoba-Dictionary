using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kotoba.Core;

namespace Kotoba.Linux;

internal static class Smoke
{
    public static async Task Run(MainWindow window)
    {
        var root = Path.GetDirectoryName(Program.Paths.Data)!;
        Check(AssetLoader.Exists(new Uri("avares://kotoba/Assets/NotoSerifCJKjp-Regular.otf")), "Japanese font is missing");
        var counts = window.Dictionary.Counts();
        Check(counts.Kanji >= 6286 && counts.Words >= 55915, "Dictionary did not load");
        using var input = new X11Input();
        window.Route("--search");
        window.SearchInput!.Text = "сигнал";
        await Until(() => window.SearchResults?.ItemsSource?.OfType<DictionaryEntry>()
            .Any(item => item.Meaning.Contains("сигнал", StringComparison.OrdinalIgnoreCase)) == true);
        window.SearchInput.Text = "несуществующий запрос";
        window.SearchInput!.Text = "きゅうてき";
        await Until(() => window.SearchResults?.ItemsSource?.OfType<DictionaryEntry>()
            .Any(item => item.Id == "word-13472") == true);
        var result = window.SearchResults!;
        result.SelectedItem = result.ItemsSource!.OfType<DictionaryEntry>().First(item => item.Id == "word-13472");
        await Idle();
        Check(window.GetVisualDescendants().OfType<SelectableTextBlock>().Any(label => label.Text == "きゅうてき"),
              "Dictionary detail did not show the corrected reading");
        Snapshot(window, Path.Combine(root, "dictionary.png"));
        await Click(window, "Во входящие");
        Check(window.Learning.Records("inbox").Any(item => item.Id == "word-13472"), "Save to Inbox failed");
        var headword = window.GetVisualDescendants().OfType<InteractiveHeadword>().Single();
        var glyphs = headword.Inlines!.OfType<Run>().ToArray();
        var initialColors = glyphs.Select(glyph => glyph.Foreground).ToArray();
        input.Move(headword.PointToScreen(headword.TextLayout.HitTestTextRange(0, 1).Single().Center));
        await Until(() => !Equals(glyphs[0].Foreground, initialColors[0]));
        Check(Equals(glyphs[1].Foreground, initialColors[1]), "Hover changed a neighboring kanji");
        headword.SelectAll();
        Check(headword.SelectedText == "旧敵", "Kanji links broke full-word selection");
        input.Click();
        await Until(() => window.GetVisualDescendants().OfType<ItemsControl>()
            .Any(control => control.ItemsSource?.OfType<DictionaryEntry>().Any(item => item.Id == "word-13472") == true));
        Check(window.GetVisualDescendants().OfType<SelectableTextBlock>()
                  .Any(label => label.Text == "旧"), "Clicking a kanji did not open its card");
        await Click(window, "Назад");
        await Click(window, "Назад");
        window.Route("yarxi-study://entry/kanji-76");
        await Idle();
        var related = window.GetVisualDescendants().OfType<ItemsControl>().Single();
        var allRelated = window.Dictionary.WordsContainingKanji("一");
        Check(allRelated.Count > 500 && related.ItemsSource!.OfType<DictionaryEntry>().SequenceEqual(allRelated),
              "Inline related words must include every match");
        Check(related.GetVisualDescendants().OfType<Button>().Count() < allRelated.Count,
              "Related-word rows were created outside the viewport");
        await Click(window, "Сегодня");
        await Click(window, "Начать повторение");
        await Click(window, "Показать перевод · Пробел");
        await Click(window, "2  Помню");
        Check(window.Learning.Records().Any(item => item.Id == "word-13472" && item.Card is not null),
              "Review score did not persist");
        await Click(window, "Отменить оценку");
        Check(window.Learning.Session()?.CurrentId == "word-13472", "Undo did not restore the card");
        window.Route("yarxi-study://setup");
        await Idle();
        await CheckShortcutSettings(window);
        await Click(window, "Малый");
        var card = window.CardWindow ?? throw new InvalidOperationException("Desktop card was not opened");
        var previous = card.DisplayedId;
        await Click(card, "Сохранить");
        Check(card.DisplayedId == previous && window.Learning.Records().Any(item => item.Id == previous),
              "Card save changed the selection or did not persist");
        await Click(card, "Дальше");
        Check(card.DisplayedId is not null && card.DisplayedId != previous, "Next did not advance the desktop card");
        await Click(window, "Большой");
        Check(ReferenceEquals(window.CardWindow, card), "Changing size opened another card");
        Snapshot(card, Path.Combine(root, "card.png"));
        var persistedCard = new DesktopCardStore(Program.Paths.Card, window.Dictionary, () => window.Learning);
        Check(persistedCard.Cards.Single().Size == "Large", "Desktop card settings did not persist");
        window.Close();
        await Idle();
        Check(!window.IsVisible && card.IsVisible, "Closing the main window did not leave the card running");
        card.GetVisualDescendants().OfType<Button>().Single(button => button.Content is TextBlock)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle();
        Check(window.IsVisible, "Opening a card entry did not restore the main window");
        window.Close();
        input.SearchShortcut();
        await Until(() => window.IsVisible && window.SearchInput?.SelectedText == "きゅうてき");
        Console.WriteLine($"PASS: Linux desktop, {counts.Kanji} kanji and {counts.Words} words, debounced search, kanji links, FSRS/Undo, desktop card, X11 shortcut");
    }

    private static async Task CheckShortcutSettings(MainWindow window)
    {
        var next = new DictionaryShortcut(DictionaryShortcut.Control | DictionaryShortcut.Alt, 'K');
        using var blocker = new GlobalDictionaryShortcut(() => { });
        blocker.Change(next, () => { });
        var picker = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(control => control.SelectedItem?.ToString() == "J");
        picker.SelectedIndex++;
        await Click(window, "Применить");
        Check(Settings.Load(Program.Paths.Settings).DictionaryHotkey == DictionaryShortcut.Default,
              "A conflicting shortcut changed the stored binding");
        blocker.Change(null, () => { });
        await Click(window, "Применить");
        Check(Settings.Load(Program.Paths.Settings).DictionaryHotkey == next, "Shortcut change did not persist");
        await Click(window, "Отключить");
        Check(Settings.Load(Program.Paths.Settings).DictionaryHotkey is null, "Shortcut disable did not persist");
        blocker.Change(next, () => { });
        await Click(window, "По умолчанию");
        Check(Settings.Load(Program.Paths.Settings).DictionaryHotkey == DictionaryShortcut.Default,
              "Default shortcut was not restored");
    }

    private sealed class X11Input : IDisposable
    {
        private readonly IntPtr display = XOpenDisplay(IntPtr.Zero);

        public void Move(PixelPoint point)
        {
            XTestFakeMotionEvent(display, -1, point.X, point.Y, 0);
            XFlush(display);
        }

        public void Click()
        {
            XTestFakeButtonEvent(display, 1, 1, 0);
            XTestFakeButtonEvent(display, 1, 0, 0);
            XFlush(display);
        }

        public void SearchShortcut()
        {
            var keys = new[] { 0xffe3u, 0xffe9u, (uint)'j' }.Select(symbol => XKeysymToKeycode(display, symbol)).ToArray();
            foreach (var key in keys)
                XTestFakeKeyEvent(display, key, 1, 0);
            foreach (var key in keys.Reverse())
                XTestFakeKeyEvent(display, key, 0, 0);
            XFlush(display);
        }

        public void Dispose() => XCloseDisplay(display);

        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
        [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint symbol);
        [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint key, int pressed, nuint delay);
        [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, int pressed, nuint delay);
        [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    }

    private static Task Idle() => Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).GetTask();

    private static async Task Click(Window window, string label)
    {
        await Idle();
        var button = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(control => Equals(control.Content, label));
        Check(button is not null, $"Button not found: {label}");
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Idle();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(30);
        Check(condition(), "Timed out waiting for search results");
    }

    private static void Snapshot(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(path);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
