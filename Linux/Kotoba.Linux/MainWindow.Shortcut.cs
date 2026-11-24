using Avalonia.Controls;
using Kotoba.Core;
using static Kotoba.Linux.Ui;

namespace Kotoba.Linux;

internal sealed partial class MainWindow
{
    private static readonly (uint Flag, string Name)[] ShortcutModifiers =
        [(DictionaryShortcut.Control, "Ctrl"), (DictionaryShortcut.Alt, "Alt"),
         (DictionaryShortcut.Shift, "Shift"), (DictionaryShortcut.Windows, "Super")];
    private static readonly (uint Value, string Label)[] ShortcutKeys =
        Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat([0x20])
            .Concat(Enumerable.Range(0x70, 24)).Where(value => DictionaryShortcut.SupportsKey((uint)value))
            .Select(value => ((uint)value, KeyLabel((uint)value))).ToArray();
    private GlobalDictionaryShortcut? shortcut;
    private string? shortcutError;

    private void StartShortcut()
    {
        if (GlobalDictionaryShortcut.UsesWayland)
            return;
        try
        {
            shortcut = new(() => Route("--search"));
            shortcut.Change(settings.DictionaryHotkey, () => { });
        }
        catch (Exception error)
        {
            shortcutError = error.Message;
        }
    }

    private Control ShortcutSection()
    {
        var details = Column(Text("Сочетания клавиш", 21), Text("Ctrl+F открывает поиск в Kotoba."));
        if (shortcut is null)
        {
            if (shortcutError is not null)
                details.Children.Add(Text(shortcutError, 13, Accent));
            details.Children.Add(Text("Для вызова из других приложений назначьте команду kotoba --search в настройках рабочего стола."));
            return Card(details);
        }

        var selected = settings.DictionaryHotkey ?? DictionaryShortcut.Default;
        var modifiers = ShortcutModifiers.Select(item =>
            new CheckBox { Content = item.Name, IsChecked = (selected.Modifiers & item.Flag) != 0 }).ToArray();
        var key = Choices(ShortcutKeys, selected.VirtualKey);
        var status = Text(ShortcutStatus(), 13, shortcutError is null ? Muted : Accent);
        var disable = new Button { Content = "Отключить", IsEnabled = settings.DictionaryHotkey is not null };
        disable.Click += (_, _) => Change(null);
        details.Children.Add(Row(modifiers));
        details.Children.Add(Row(key, Button("Применить", Apply, true), Button("По умолчанию", () =>
        {
            if (Change(DictionaryShortcut.Default))
            {
                for (int i = 0; i < modifiers.Length; i++)
                    modifiers[i].IsChecked = (DictionaryShortcut.Default.Modifiers & ShortcutModifiers[i].Flag) != 0;
                key.SelectedIndex = Array.FindIndex(ShortcutKeys, item => item.Value == DictionaryShortcut.Default.VirtualKey);
            }
        }), disable));
        details.Children.Add(status);
        return Card(details);

        void Apply()
        {
            uint flags = 0;
            for (int i = 0; i < modifiers.Length; i++)
                if (modifiers[i].IsChecked == true)
                    flags |= ShortcutModifiers[i].Flag;
            Change(new DictionaryShortcut(flags, Value(key, selected.VirtualKey)));
        }

        bool Change(DictionaryShortcut? updated)
        {
            try
            {
                shortcut.Change(updated, () => SaveSettings(value => value.DictionaryHotkey = updated));
                shortcutError = null;
            }
            catch (Exception error) { shortcutError = error.Message; }
            status.Text = ShortcutStatus();
            status.Foreground = shortcutError is null ? Muted : Accent;
            disable.IsEnabled = settings.DictionaryHotkey is not null;
            return shortcutError is null;
        }
    }

    private string ShortcutStatus()
    {
        if (shortcutError is not null)
            return shortcutError;
        if (settings.DictionaryHotkey is not { } value)
            return "Глобальное сочетание отключено.";
        var keys = ShortcutModifiers.Where(item => (value.Modifiers & item.Flag) != 0)
                                    .Select(item => item.Name).Append(KeyLabel(value.VirtualKey));
        return $"Текущее сочетание: {string.Join("+", keys)}";
    }

    private static string KeyLabel(uint key) => key switch
    {
        0x20 => "Пробел",
        >= 0x70 => $"F{key - 0x70 + 1}",
        _ => ((char)key).ToString()
    };
}
