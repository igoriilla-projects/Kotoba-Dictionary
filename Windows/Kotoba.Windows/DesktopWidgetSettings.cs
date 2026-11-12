using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kotoba.Core;

namespace Kotoba.Windows;
public sealed class DesktopWidgetSettings : Window
{
    public DesktopWidgetSettings(DesktopWidgetManager manager,string id)
    {
        Title=UiText.T("Edit Kotoba widget");Width=440;Height=690;MinWidth=400;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Style=(Style)FindResource(typeof(Window));var c=manager.Store.Get(id);
        var form=new StackPanel{Margin=new Thickness(24)};
        var title=new TextBlock{Text=Title,FontFamily=new FontFamily("Georgia"),FontSize=24,Margin=new Thickness(0,0,0,20)};form.Children.Add(title);
        ComboBox Choice(string name,IEnumerable<string> values,string selected)
        {
            form.Children.Add(new TextBlock{Text=UiText.T(name),Margin=new Thickness(0,8,0,6)});
            var combo=new ComboBox{ItemsSource=values,SelectedItem=selected,Margin=new Thickness(0,0,0,6)};UiText.Options(combo);form.Children.Add(combo);return combo;
        }
        var size=Choice("Size",["Small","Medium","Large"],c.Size);
        var content=Choice("Content",["Mixed","Kanji","Words"],c.Content);
        var feed=Choice("Source",["Dictionary","Learning list"],c.Feed);
        var listLabel=new TextBlock{Text=UiText.T("Learning list"),Margin=new Thickness(0,8,0,6)};form.Children.Add(listLabel);
        var lists=new List<LearningList>{new("inbox",UiText.T("Inbox"))};string? learningError=null;
        try{lists.AddRange(manager.Store.Lists());}catch(Exception ex){learningError=ex.Message;}
        if(!lists.Any(l=>l.Id==c.ListId))lists.Add(new(c.ListId,UiText.T("Deleted list — choose another")));
        var list=new ComboBox{ItemsSource=lists,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=c.ListId};form.Children.Add(list);
        void FeedChanged(){list.IsEnabled=listLabel.IsEnabled=(string)feed.SelectedItem=="Learning list";}
        feed.SelectionChanged+=(_,_)=>FeedChanged();FeedChanged();
        if(learningError!=null)form.Children.Add(new TextBlock{Text=UiText.T("Learning storage is unavailable. Reopen this editor after retrying access in Kotoba."),TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("Accent"),Margin=new Thickness(0,8,0,8)});
        var reading=Choice("Reading format",["Hiragana","Rōmaji"],c.Romaji?"Rōmaji":"Hiragana");
        var intervals=new[]{("Off · manual only",0),("Every 5 minutes",300),("Every 15 minutes",900),("Every 30 minutes",1800),("Every hour",3600),("Every 6 hours",21600),("Every day",86400)};
        var refresh=Choice("Refresh timer",intervals.Select(i=>i.Item1),intervals.First(i=>i.Item2==c.RefreshSeconds).Item1);
        TextBox Percent(string label,int value)
        {form.Children.Add(new TextBlock{Text=UiText.T(label+" (%) · 50–150"),Margin=new Thickness(0,10,0,6)});var box=new TextBox{Text=value.ToString()};form.Children.Add(box);return box;}
        var word=Percent("Word / kanji size",c.HeadwordPercent);var kana=Percent("Reading size",c.ReadingPercent);var translation=Percent("Translation size",c.TranslationPercent);
        CheckBox Toggle(string label,bool value){var box=new CheckBox{Content=UiText.T(label),IsChecked=value,Margin=new Thickness(0,14,0,0)};form.Children.Add(box);return box;}
        var showReading=Toggle("Show reading",c.ShowReading);var showTranslation=Toggle("Show translation",c.ShowTranslation);var topmost=Toggle("Always on top",c.Topmost);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=(Brush)FindResource("Accent"),Margin=new Thickness(0,0,0,8)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,20,0,0)};
        var done=new Button{Content=UiText.T("Done"),IsDefault=true,Style=(Style)FindResource("Primary"),Margin=new Thickness(0,0,10,0)};
        var cancel=new Button{Content=UiText.T("Cancel"),IsCancel=true};buttons.Children.Add(done);buttons.Children.Add(cancel);
        done.Click+=(_,_)=>{
            try
            {
                int Read(TextBox box){if(!int.TryParse(box.Text,out int value)||value<50||value>150)throw new InvalidOperationException("Enter whole percentages between 50 and 150.");return value;}
                // Read the current position/selection at commit time; a timer may have advanced during editing.
                manager.Store.Update(manager.Store.Get(id) with {Size=(string)size.SelectedItem,Content=(string)content.SelectedItem,Feed=(string)feed.SelectedItem,ListId=(string)list.SelectedValue,
                    Romaji=reading.SelectedIndex==1,RefreshSeconds=intervals[refresh.SelectedIndex].Item2,HeadwordPercent=Read(word),ReadingPercent=Read(kana),TranslationPercent=Read(translation),
                    ShowReading=showReading.IsChecked==true,ShowTranslation=showTranslation.IsChecked==true,Topmost=topmost.IsChecked==true});DialogResult=true;
            }
            catch(Exception ex){error.Text=UiText.T(ex.Message);}
        };
        var root=new Grid{Background=(Brush)FindResource("Paper")};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        root.Children.Add(new ScrollViewer{Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var footer=new StackPanel{Margin=new Thickness(24,8,24,20)};buttons.Margin=new Thickness(0);footer.Children.Add(error);footer.Children.Add(buttons);Grid.SetRow(footer,1);root.Children.Add(footer);Content=root;
    }
}
