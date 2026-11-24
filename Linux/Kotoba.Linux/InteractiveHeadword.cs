using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Kotoba.Core;

namespace Kotoba.Linux;

internal sealed class InteractiveHeadword : SelectableTextBlock
{
    private static readonly Cursor Hand = new(StandardCursorType.Hand);
    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);
    private static readonly IBrush HoverColor = Brush.Parse("#5B7382");
    private readonly List<Link> links = [];
    private readonly Action<DictionaryEntry> open;
    private Link? hovered;
    private Link? pressed;
    private Point pressPosition;

    private sealed record Link(Run Run, int Offset, int Length, DictionaryEntry Entry);

    internal InteractiveHeadword(string text, double size, IReadOnlyList<DictionaryEntry> kanji,
                                 Action<DictionaryEntry> open)
    {
        this.open = open;
        FontFamily = Ui.Japanese;
        FontSize = size;
        TextWrapping = TextWrapping.Wrap;
        Cursor = TextCursor;
        var entries = kanji.ToDictionary(entry => entry.Text);
        var characters = StringInfo.GetTextElementEnumerator(text);
        while (characters.MoveNext())
        {
            string character = characters.GetTextElement();
            var run = new Run(character);
            if (entries.TryGetValue(character, out var entry))
                links.Add(new Link(run, characters.ElementIndex, character.Length, entry));
            Inlines!.Add(run);
        }
        var menuItems = kanji.Select(entry =>
        {
            var item = new MenuItem { Header = $"Открыть кандзи {entry.Text}" };
            item.Click += (_, _) => open(entry);
            return item;
        }).ToArray();
        ContextMenu = new ContextMenu { ItemsSource = menuItems };
    }

    private Link? LinkAt(Point point) =>
        links.FirstOrDefault(link => TextLayout.HitTestTextRange(link.Offset, link.Length)
            .Any(bounds => bounds.Contains(point)));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(point.X - pressPosition.X) > 4 || Math.Abs(point.Y - pressPosition.Y) > 4)
                pressed = null;
            return;
        }
        Hover(LinkAt(point));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        pressed = null;
        Hover(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        pressPosition = e.GetPosition(this);
        pressed = e.ClickCount == 1 && e.KeyModifiers == KeyModifiers.None &&
                  e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? LinkAt(pressPosition) : null;
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var clicked = pressed;
        pressed = null;
        var point = e.GetPosition(this);
        if (clicked is not null && e.InitialPressMouseButton == MouseButton.Left &&
            Math.Abs(point.X - pressPosition.X) <= 4 && Math.Abs(point.Y - pressPosition.Y) <= 4 &&
            LinkAt(point) == clicked && SelectionStart == SelectionEnd)
        {
            e.Handled = true;
            open(clicked.Entry);
        }
    }

    private void Hover(Link? link)
    {
        if (link == hovered)
            return;
        hovered?.Run.ClearValue(TextElement.ForegroundProperty);
        hovered = link;
        if (link is not null)
            link.Run.Foreground = HoverColor;
        Cursor = link is null ? TextCursor : Hand;
        ToolTip.SetTip(this, link is null ? null : $"Открыть кандзи {link.Entry.Text}");
    }
}
