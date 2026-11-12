using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Kotoba.Windows;

/// Display-only localization: stored page/content/feed IDs and user list names stay intact.
internal static class UiText
{
    private static readonly Dictionary<string,string> Words=new()
    {
        ["Today"]="Сегодня",["My Lists"]="Мои списки",["Dictionary"]="Словарь",["Settings"]="Настройки",
        ["A little practice, today."]="Немного практики сегодня.",["JAPANESE–RUSSIAN"]="ЯПОНСКО–РУССКИЙ",["少しずつ · LITTLE BY LITTLE"]="少しずつ · ПОНЕМНОГУ КАЖДЫЙ ДЕНЬ",
        ["Search"]="Поиск",["Explore"]="Обзор",["Mixed"]="Все записи",["Kanji"]="Кандзи",["Words"]="Слова",["Hiragana"]="Хирагана",["Rōmaji"]="Ромадзи",
        ["Inbox"]="Входящие",["All saved"]="Все сохранённые",["New list"]="Новый список",["Rename list"]="Переименовать список",["Rename"]="Переименовать",["Delete list"]="Удалить список",
        ["Save"]="Сохранить",["Save to Inbox"]="Во входящие",["Saved"]="Сохранено",["✓ Saved"]="✓ Сохранено",["Organize"]="По спискам",["Remove from saved entries"]="Убрать из сохранённых",["Create a list in My Lists"]="Создайте список в разделе «Мои списки»",
        ["Start Review"]="Начать повторение",["Continue Review"]="Продолжить повторение",["Pause review"]="Прервать повторение",["Review this list"]="Повторить список",["Undo latest rating"]="Отменить оценку",
        ["Again"]="Ещё раз",["Good"]="Помню",["Easy"]="Легко",["1 Again · 2 Good · 3 Easy"]="1 Ещё раз · 2 Помню · 3 Легко",["Reveal meaning · Space"]="Показать перевод · Пробел",
        ["due reviews"]="к повторению",["new entries remaining today"]="новых записей осталось на сегодня",["Check for due reviews"]="Проверить повторения",
        ["Save entries from the dictionary. Review them here, a little each day."]="Сохраняйте записи из словаря и повторяйте их здесь понемногу каждый день.",
        ["You're caught up for now."]="На сегодня всё готово.",["Come back when reviews are due, or save more entries for a future day."]="Возвращайтесь, когда подойдёт время повторения, или сохраните новые записи.",
        ["Done"]="Готово",["Back"]="Назад",["Cancel"]="Отмена",["Copy"]="Копировать",["Copy word / kanji"]="Копировать",["Copied"]="Скопировано",["Copy failed"]="Не скопировано",
        ["Copy failed. Try again."]="Не удалось скопировать. Нажмите ещё раз.",["Copy entire entry: "]="Скопировать целиком: ",["Copied to clipboard: "]="Скопировано в буфер: ",
        ["Select text to copy, or click a kanji to explore it."]="Выделите текст для копирования или нажмите на кандзи, чтобы разобрать его.",["Open kanji: "]="Открыть кандзи: ",
        ["Kanji & words"]="Кандзи и слова",["No related words."]="В словаре нет доступных слов с этим кандзи.",
        ["Retry"]="Повторить",["Retry learning storage"]="Повторить подключение",["Retry widgets"]="Повторить подключение виджета",
        ["No results"]="Ничего не найдено",["Try another Japanese, hiragana, rōmaji, or Russian search."]="Попробуйте другой запрос на японском, хираганой, ромадзи или по-русски.",
        ["No entry available"]="Нет доступных записей",["Try another content filter."]="Выберите другой тип записей.",
        ["No saved entries here"]="Здесь пока нет записей",["Save entries in Dictionary. Use Organize on an entry to assign lists."]="Сохраните записи в словаре. Меню «По спискам» распределяет их по спискам.",
        ["Search Japanese, hiragana, rōmaji, or Russian…"]="Поиск: японский, хирагана, ромадзи или русский…",["Search Japanese, hiragana, rōmaji, or Russian"]="Поиск по словарю",["Search saved entries…"]="Поиск в сохранённых…",["Search saved entries"]="Поиск в сохранённых",
        ["KANJI / 漢字"]="КАНДЗИ / 漢字",["WORD / 言葉"]="СЛОВО / 言葉",["Next entry  →"]="Дальше  →",["Next entry"]="Следующая запись",["Hide translation"]="Скрыть перевод",["Show translation"]="Показать перевод",["Reveal translation"]="Показать перевод",["Auto-refresh off"]="Автообновление выключено",
        ["Hiragana readings · Russian translations"]="Чтение хираганой · Перевод на русский",["Rōmaji readings · Russian translations"]="Чтение ромадзи · Перевод на русский",
        ["Text size"]="Размер текста",["Word / kanji"]="Слово / кандзи",["Word / kanji size"]="Размер слова / кандзи",["Reading size"]="Размер чтения",["Translation size"]="Размер перевода",["Reading"]="Чтение",["Translation"]="Перевод",["Reset text sizes"]="Сбросить размеры",
        ["Learning"]="Обучение",["Reading format"]="Формат чтения",["Refresh timer"]="Таймер обновления",["Hiragana readings"]="Чтение хираганой",["Off"]="Вручную",["Off · manual only"]="Вручную",
        ["Every 5 minutes"]="Каждые 5 минут",["Every 15 minutes"]="Каждые 15 минут",["Every 30 minutes"]="Каждые 30 минут",["Every hour"]="Каждый час",["Every 6 hours"]="Каждые 6 часов",["Every day"]="Каждый день",
        ["Shared across every list, plus all due reviews. First rating counts; saving and browsing do not. Review fronts always use hiragana."]="Общий лимит для всех списков плюс все записи к повторению. Учитывается первая оценка, а не сохранение или просмотр. При повторении всегда используется хирагана.",
        ["Automatically show another random card while Dictionary → Explore is open and the app is active. Choosing Off keeps each card until you press Next."]="Случайная карточка меняется, пока открыт раздел «Словарь → Обзор» и приложение активно. В режиме «Вручную» карточка меняется только кнопкой «Дальше».",
        ["Readings are transcribed from the dictionary’s rōmaji. Long vowels use ー where the source does not specify their kana spelling. Rōmaji remains available above."]="Чтение получено из ромадзи словаря. Если точное написание долгой гласной неизвестно, используется ー. Исходное ромадзи доступно в настройках выше.",
        ["Kotoba for Windows"]="Kotoba для Windows",["Your lists and review progress are stored locally on this PC."]="Ваши списки и прогресс хранятся только на этом компьютере.",
        ["Desktop widget"]="Виджет рабочего стола",["Small"]="Малый",["Medium"]="Средний",["Large"]="Большой",["Edit"]="Изменить",["Remove"]="Убрать",["Size"]="Размер",["Content"]="Тип записей",["Source"]="Источник",["Learning list"]="Список обучения",
        ["Choose a size to show or resize your desktop card. Right-click it to change its source, fonts or timer. Drag its KOTOBA header to move it."]="Выберите размер единственного виджета. Правая кнопка мыши открывает настройки источника, текста и таймера. Перетащите заголовок KOTOBA, чтобы переместить виджет.",
        ["Closing the main window keeps the card running. Use the Kotoba tray menu to reopen it or quit."]="После закрытия главного окна виджет продолжает работать. Меню Kotoba в области уведомлений открывает окно или завершает приложение.",
        ["Edit Kotoba widget"]="Настройки виджета Kotoba",["Edit widget"]="Настроить виджет",["Edit widget…"]="Настроить виджет…",["Open Kotoba"]="Открыть Kotoba",["Quit Kotoba"]="Выйти из Kotoba",["Show widget"]="Показать виджет",["Remove widget"]="Убрать виджет",
        ["Always on top"]="Поверх остальных окон",["Show reading"]="Показывать чтение",["Deleted list — choose another"]="Список удалён — выберите другой",
        ["Enter whole percentages between 50 and 150."]="Введите целые проценты от 50 до 150.",["Drag to move · Right-click to edit"]="Перетащите для перемещения · Правая кнопка — настройки",
        ["Open this entry in Kotoba"]="Открыть запись в Kotoba",["Open My Lists"]="Открыть мои списки",["Save this entry to Inbox"]="Сохранить запись во входящие",
        ["No entries in this list. Right-click → Edit widget to choose a list or content."]="В списке нет записей. Правая кнопка → Настроить виджет: выберите другой список или тип записей.",
        ["Learning storage is unavailable. Reopen this editor after retrying access in Kotoba."]="Нет доступа к данным обучения. Повторите подключение в Kotoba и откройте настройки снова.",
        ["Learning storage is unavailable. Use Retry."]="Нет доступа к данным обучения. Повторите подключение.",["Desktop widget settings are unavailable. Use Retry."]="Настройки виджета недоступны. Повторите подключение.",
        ["This learning database requires a newer version of Kotoba."]="Для этой базы обучения нужна более новая версия Kotoba.",["This entry is not available in the dictionary."]="Эта запись недоступна в словаре.",
        ["Enter a list name."]="Введите название списка.",["Save this entry first."]="Сначала сохраните запись.",["Start a review first."]="Сначала начните повторение.",["Reveal the current card before rating it."]="Покажите перевод текущей карточки перед оценкой.",
        ["This card changed. Continue the review to reload it."]="Карточка изменилась. Продолжите повторение, чтобы обновить её.",["Today's new-entry allowance is used up."]="Лимит новых записей на сегодня исчерпан.",["There is no rating to undo."]="Нет оценки для отмены.",["A later review changed this entry."]="Более позднее повторение изменило эту запись.",
        ["This learning list was deleted. Right-click → Edit widget to choose another list."]="Список обучения удалён. Правая кнопка → Настроить виджет: выберите другой список.",
        ["Desktop widget settings are empty."]="Настройки виджета пусты.",["Unsupported desktop widget settings version."]="Версия настроек виджета не поддерживается.",["Desktop widget settings are missing."]="Настройки виджета отсутствуют.",["Desktop widget identities are invalid."]="Повреждены идентификаторы виджета.",
        ["The tray icon is unavailable. Use a card's menu to reopen or quit Kotoba."]="Значок в области уведомлений недоступен. Меню виджета позволяет открыть Kotoba или выйти.",
        ["Kotoba desktop widgets"]="Виджет Kotoba",["Kotoba desktop widget"]="Виджет Kotoba",["JAPANESE · RU"]="ЯПОНСКИЙ · РУ",
    };
    public static string T(string? text)
    {
        if(text==null)return "";
        if(Words.TryGetValue(text,out var translated))return translated;
        var patterns=new(string Pattern,string Replacement)[]{
            (@"^([\d\s,.]+) kanji$","Кандзи: $1"),(@"^([\d\s,.]+) words$","Слов: $1"),(@"^(\d+) strokes$","Черт: $1"),
            (@"^(\d+) results( · first 100 shown)?$","Найдено: $1$2"),(@"^(\d+) entries$","Записей: $1"),
            (@"^(\d+) saved entries waiting to be learned · due reviews come first$","Сохранено новых записей: $1 · сначала записи к повторению"),
            (@"^New entries per day: (\d+)$","Новых записей в день: $1"),(@"^Next card in (.*)$","Следующая карточка через $1"),
            (@"^(\d+) min$","$1 мин"),(@"^(\d+) hr$","$1 ч"),(@"^(\d+) days$","$1 дн."),
            (@"^Version ([\d.]+) · Offline Japanese–Russian dictionary$","Версия $1 · Японско-русский словарь без интернета"),
        };
        foreach(var (pattern,replacement) in patterns)if(Regex.IsMatch(text,pattern))return Regex.Replace(text,pattern,replacement).Replace(" · first 100 shown"," · первые 100");
        foreach(var (from,to) in new[]{("Could not read settings: ","Не удалось прочитать настройки: "),("Could not load dictionary: ","Не удалось открыть словарь: "),("Could not read dictionary: ","Не удалось прочитать словарь: "),("Could not restore desktop widgets: ","Не удалось восстановить виджет: "),("Tray is unavailable: ","Область уведомлений недоступна: "),("Could not contact the running Kotoba window. ","Не удалось связаться с открытым окном Kotoba. ")})
            if(text.StartsWith(from))return to+T(text[from.Length..]);
        if(text.StartsWith("Learning storage is unavailable. Check access to "))return text.Replace("Learning storage is unavailable. Check access to ","Нет доступа к данным обучения. Проверьте доступ к ").Replace(" and Retry. "," и повторите подключение. ");
        const string percentSuffix=" (%) · 50–150";
        if(text.EndsWith(percentSuffix))return T(text[..^percentSuffix.Length])+percentSuffix;
        if(text.EndsWith(" size"))return "Размер: "+T(text[..^5]);
        if(text.EndsWith(" widget"))return "Виджет: "+T(text[..^7]);
        if(text.Contains(" · "))return string.Join(" · ",text.Split(" · ").Select(s=>Words.GetValueOrDefault(s,s)));
        return text;
    }
    public static void Options(ComboBox combo)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding{Converter=new Converter()});
        combo.ItemTemplate=new DataTemplate{VisualTree=text};
    }
    private sealed class Converter:IValueConverter
    {public object Convert(object value,Type target,object parameter,CultureInfo culture)=>T(value?.ToString());public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();}
}
