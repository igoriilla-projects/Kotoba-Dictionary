using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Kotoba.Core;

namespace Kotoba.Windows;
/// Inline rows use their card's scroll host, with no nested viewport or result cap.
internal sealed class RelatedWordsView:StackPanel
{
    internal IReadOnlyList<DictionaryEntry> Words {get;}
    internal ListBox List {get;}
    internal RelatedWordsView(DictionaryEntry kanji,DictionaryStore dictionary,Settings preferences,bool romaji,Action<DictionaryEntry> open)
    {
        Children.Add(new TextBlock{Text="Слова с кандзи "+kanji.Text,FontSize=22,Margin=new Thickness(0,12,0,16)});
        try{Words=dictionary.WordsContainingKanji(kanji.Text);}
        catch(Exception ex){Words=[];Children.Add(new TextBlock{Text=UiText.T(ex.Message),TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("Accent")});}
        Children.Add(new TextBlock{Text=$"Найдено слов: {Words.Count}",FontSize=12,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,0,0,16)});
        if(Words.Count==0)Children.Add(new TextBlock{Text=UiText.T("No related words."),TextWrapping=TextWrapping.Wrap});
        List=new ListBox{ItemsSource=Words,BorderThickness=new Thickness(0),Background=new SolidColorBrush(Color.FromArgb(115,255,255,255)),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        // The default ListBox template introduces its own ScrollViewer. Remove
        // it entirely so wheel/keyboard scrolling belongs to the containing card.
        var border=new FrameworkElementFactory(typeof(Border));border.SetBinding(Border.BackgroundProperty,new Binding("Background"){RelativeSource=new RelativeSource(RelativeSourceMode.TemplatedParent)});border.SetValue(Border.CornerRadiusProperty,new CornerRadius(12));border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        List.Template=new ControlTemplate(typeof(ListBox)){VisualTree=border};
        List.ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        VirtualizingPanel.SetIsVirtualizing(List,false);
        var style=new Style(typeof(ListBoxItem));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12)));style.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(0,0,0,1)));style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromArgb(24,41,51,46))));List.ItemContainerStyle=style;
        var panel=new FrameworkElementFactory(typeof(DockPanel));
        var word=new FrameworkElementFactory(typeof(TextBlock));word.SetBinding(TextBlock.TextProperty,new Binding("Text"));word.SetValue(TextBlock.FontFamilyProperty,FindResource("Japanese"));word.SetValue(TextBlock.FontSizeProperty,25*preferences.HeadwordScale);word.SetValue(FrameworkElement.WidthProperty,150.0);word.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);word.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,18,0));panel.AppendChild(word);
        var definitions=new FrameworkElementFactory(typeof(StackPanel));
        var reading=new FrameworkElementFactory(typeof(TextBlock));reading.SetBinding(TextBlock.TextProperty,new Binding{Converter=new ReadingConverter(romaji)});reading.SetValue(TextBlock.FontSizeProperty,12*preferences.ReadingScale);reading.SetValue(TextBlock.ForegroundProperty,FindResource("Accent"));reading.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);definitions.AppendChild(reading);
        var meaning=new FrameworkElementFactory(typeof(TextBlock));meaning.SetBinding(TextBlock.TextProperty,new Binding("Meaning"));meaning.SetValue(TextBlock.FontSizeProperty,13*preferences.TranslationScale);meaning.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);meaning.SetValue(TextBlock.MaxHeightProperty,44*preferences.TranslationScale);meaning.SetValue(FrameworkElement.MarginProperty,new Thickness(0,5,0,0));definitions.AppendChild(meaning);panel.AppendChild(definitions);List.ItemTemplate=new DataTemplate{VisualTree=panel};
        List.PreviewMouseLeftButtonUp+=(_,e)=>{
            var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if(item?.DataContext is DictionaryEntry entry){open(entry);e.Handled=true;}
        };
        List.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&List.SelectedItem is DictionaryEntry entry){open(entry);e.Handled=true;}};
        List.ContextMenuOpening+=(_,e)=>{
            var item=Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if(item?.DataContext is not DictionaryEntry entry){e.Handled=true;return;}
            var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header=new CopyHeadwordButton(entry.Text),StaysOpenOnClick=true});item.ContextMenu=menu;List.ContextMenu=menu;
        };
        List.ContextMenu=new ContextMenu();Children.Add(List);
    }
    private static T? Ancestor<T>(DependencyObject? node) where T:DependencyObject
    {while(node!=null){if(node is T match)return match;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}return null;}
    private sealed class ReadingConverter(bool romaji):IValueConverter
    {public object Convert(object value,Type target,object parameter,CultureInfo culture)=>((DictionaryEntry)value).Read(romaji);public object ConvertBack(object value,Type target,object parameter,CultureInfo culture)=>throw new NotSupportedException();}
}
