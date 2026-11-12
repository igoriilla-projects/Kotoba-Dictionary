using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kotoba.Core;

namespace Kotoba.Windows;
internal sealed class KanjiLookupWindow:Window
{
    private readonly MainWindow main;
    private readonly List<DictionaryEntry> path=[];
    private readonly Grid body=new();
    private readonly Button back;
    internal DictionaryEntry Current=>path[^1];
    internal int Depth=>path.Count;
    public KanjiLookupWindow(MainWindow main,DictionaryEntry entry)
    {
        this.main=main;Owner=main;Title=UiText.T("Kanji & words");Style=(Style)FindResource(typeof(Window));
        Width=720;Height=700;MinWidth=620;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{Background=(Brush)FindResource("Paper")};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new DockPanel{Margin=new Thickness(20)};
        back=new Button{Content=UiText.T("Back"),Margin=new Thickness(0,0,12,0)};back.Click+=(_,_)=>Back();header.Children.Add(back);
        var done=new Button{Content=UiText.T("Done"),IsCancel=true};done.Click+=(_,_)=>Close();DockPanel.SetDock(done,Dock.Right);header.Children.Add(done);
        header.Children.Add(new TextBlock{Text=Title,FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});root.Children.Add(header);Grid.SetRow(body,1);root.Children.Add(body);Content=root;
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        main.Model.PropertyChanged+=ModelChanged;Closed+=(_,_)=>main.Model.PropertyChanged-=ModelChanged;Navigate(entry);
    }
    private void ModelChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)=>RenderPage();
    internal void Navigate(DictionaryEntry entry){path.Add(entry);RenderPage();}
    internal void Back(){if(path.Count>1){path.RemoveAt(path.Count-1);RenderPage();}}
    private void RenderPage(){back.Visibility=path.Count>1?Visibility.Visible:Visibility.Collapsed;body.Children.Clear();body.Children.Add(main.LookupPage(Current,Navigate));}
}
