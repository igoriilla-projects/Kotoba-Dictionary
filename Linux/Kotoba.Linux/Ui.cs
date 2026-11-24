using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kotoba.Linux;

internal static class Ui
{
    public static readonly IBrush Accent = Brush.Parse("#9D3626");
    public static readonly IBrush Muted = Brush.Parse("#5B685F");
    public static readonly FontFamily Japanese = new("avares://kotoba/Assets#Noto Serif CJK JP");

    public static TextBlock Text(string text, double size = 15, IBrush? brush = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (brush is not null)
            label.Foreground = brush;
        return label;
    }

    public static SelectableTextBlock Selectable(string text, double size, bool japanese = false)
    {
        var label = new SelectableTextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (japanese)
            label.FontFamily = Japanese;
        return label;
    }

    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        if (primary)
            button.Classes.Add("primary");
        button.Click += (_, _) => action();
        return button;
    }

    public static StackPanel Column(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    public static WrapPanel Row(params Control[] controls)
    {
        var panel = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    public static Border Card(Control contents)
    {
        var border = new Border { Child = contents, VerticalAlignment = VerticalAlignment.Top };
        border.Classes.Add("card");
        return border;
    }

    public static ScrollViewer Scroll(Control contents) => new()
    {
        Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    public static ComboBox Choices<T>(IEnumerable<(T Value, string Label)> choices, T selected)
    {
        var items = choices.Select(choice => new Choice<T>(choice.Value, choice.Label)).ToArray();
        return new ComboBox
        {
            ItemsSource = items,
            SelectedItem = items.FirstOrDefault(item => EqualityComparer<T>.Default.Equals(item.Value, selected)),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    public static T Value<T>(ComboBox box, T fallback) =>
        box.SelectedItem is Choice<T> choice ? choice.Value : fallback;

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
}
