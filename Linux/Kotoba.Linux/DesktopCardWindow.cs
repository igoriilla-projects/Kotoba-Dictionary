using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Kotoba.Core;
using static Kotoba.Linux.Ui;

namespace Kotoba.Linux;

internal sealed class DesktopCardWindow : Window
{
    private readonly MainWindow main;
    private readonly DesktopCardStore store;
    private readonly string id;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer positionWrite = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly ContentControl surface = new();
    private readonly TextBlock status = Text("", 12, Accent);
    private string? size;
    internal string? DisplayedId { get; private set; }

    public DesktopCardWindow(MainWindow main, DesktopCardStore store, DesktopCard card)
    {
        this.main = main;
        this.store = store;
        id = card.Id;
        Title = "Карточка Kotoba";
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        surface.Margin = new Thickness(14);
        Content = surface;
        Refresh();
        Opened += (_, _) =>
        {
            if (card.Left is double left && card.Top is double top)
            {
                var point = new PixelPoint((int)left, (int)top);
                if (Screens.All.Any(screen => screen.WorkingArea.Contains(point)))
                    Position = point;
            }
            refresh.Start();
        };
        PositionChanged += (_, _) => { positionWrite.Stop(); positionWrite.Start(); };
        positionWrite.Tick += (_, _) => { positionWrite.Stop(); SavePosition(); };
        refresh.Tick += (_, _) =>
        {
            try { if (store.Tick(id)) Refresh(); }
            catch (Exception error) { status.Text = error.Message; }
        };
        Closed += (_, _) =>
        {
            if (positionWrite.IsEnabled)
                SavePosition();
            positionWrite.Stop();
            refresh.Stop();
        };
    }

    private void SavePosition()
    {
        try
        {
            var old = store.Get(id);
            if (old.Left != Position.X || old.Top != Position.Y)
                store.Move(id, Position.X, Position.Y);
        }
        catch (Exception error) { main.Report(error); }
    }

    internal void Refresh()
    {
        status.Text = "";
        DisplayedId = null;
        var card = store.Get(id);
        if (size != card.Size)
        {
            (Width, Height) = card.Size switch { "Small" => (250, 245), "Large" => (400, 350), _ => (375, 255) };
            size = card.Size;
        }
        Topmost = card.Topmost;
        var edit = Button("Настройки", () => main.Perform(() => main.EditCard(id, this)));
        // Detach the shared status control before assigning it to the fresh card.
        if (status.Parent is Panel previous)
            previous.Children.Remove(status);
        var view = Column();
        try
        {
            var item = store.Display(id);
            card = store.Get(id);
            DisplayedId = item?.Id;
            if (item is not null)
            {
                var word = Text(item.Text, (card.Size == "Small" ? 26 : 34) * card.HeadwordPercent / 100.0, Accent);
                word.FontFamily = Japanese;
                word.MaxLines = 2;
                word.TextTrimming = TextTrimming.CharacterEllipsis;
                var details = Column(ButtonForEntry(word, item));
                if (card.ShowReading)
                {
                    var reading = Text(item.Read(card.Romaji), 15 * card.ReadingPercent / 100.0);
                    reading.FontFamily = Japanese;
                    details.Children.Add(reading);
                }
                if (card.ShowTranslation)
                    details.Children.Add(Text(item.Meaning, 15 * card.TranslationPercent / 100.0, Muted));
                bool saved = false;
                try { saved = store.IsSaved(item.Id); }
                catch (Exception error) { status.Text = $"Списки недоступны. {error.Message}"; }
                var actions = Row(
                    Button(saved ? "Сохранено" : "Сохранить", () => Change(() => store.Save(item.Id))),
                    Button("Дальше", () => Change(() => store.Advance(id, item.Id, card.Token)), true), edit);
                var layout = new DockPanel();
                var footer = Column(actions, status);
                DockPanel.SetDock(footer, Dock.Bottom);
                layout.Children.Add(footer);
                layout.Children.Add(Scroll(details));
                surface.Content = layout;
                return;
            }
            view.Children.Add(Text("В выбранном источнике пока нет записей.", 14, Muted));
        }
        catch (Exception error)
        {
            view.Children.Add(Text("Не удалось загрузить карточку.", 15, Accent));
            status.Text = error.Message;
        }
        view.Children.Add(Row(edit, Button("Повторить", Refresh)));
        view.Children.Add(status);
        surface.Content = Scroll(view);
    }

    private Button ButtonForEntry(TextBlock word, DictionaryEntry item)
    {
        var button = new Button
        {
            Content = word, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        button.Click += (_, _) => main.Perform(() => main.OpenCardEntry(item));
        return button;
    }

    private void Change(Action action)
    {
        try { action(); Refresh(); }
        catch (Exception error) { status.Text = error.Message; }
    }
}
