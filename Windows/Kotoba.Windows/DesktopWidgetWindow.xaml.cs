using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kotoba.Core;

namespace Kotoba.Windows;
public partial class DesktopWidgetWindow : Window
{
    private readonly DesktopWidgetManager manager;
    public string CardId {get;}
    public DesktopCard Configuration=>manager.Store.Get(CardId);
    private DictionaryEntry? entry;
    private string? token,displayedId,actionError;
    private double lastPoll,retryAt;
    internal bool Removing {get;set;}
    public DesktopWidgetWindow(DesktopWidgetManager manager,string id)
    {
        this.manager=manager;CardId=id;InitializeComponent();
        Foreground=(Brush)FindResource("Ink");FontFamily=new("Segoe UI");FontSize=13;
        AutomationProperties.SetName(this,UiText.T("Kotoba desktop widget"));AutomationProperties.SetName(NextButton,UiText.T("Next entry"));AutomationProperties.SetName(SaveButton,"Сохранить запись");AutomationProperties.SetName(EditButton,UiText.T("Edit widget"));AutomationProperties.SetAutomationId(NextButton,"Next entry");AutomationProperties.SetAutomationId(SaveButton,"Save entry");
        ContextMenuOpening+=(_,_)=>ContextMenu=Menu();ContextMenu=Menu();
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space&&Keyboard.Modifiers==ModifierKeys.None){Advance();e.Handled=true;}};
        Closing+=(_,e)=>{if(!Removing){e.Cancel=true;manager.Remove(CardId);}};
        ApplyPosition();Refresh();
    }
    private Brush Accent=>(Brush)FindResource("Accent");
    private Brush Ink=>(Brush)FindResource("Ink");
    private Brush Muted=>(Brush)FindResource("Muted");
    private TextBlock Text(string text,double size,Brush? brush=null)=>new(){Text=UiText.T(text),FontSize=size,Foreground=brush??Ink,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis};
    private TextBlock Reading(DictionaryEntry e,DesktopCard c)
    {var t=Text(e.Read(c.Romaji),12*c.ReadingPercent/100.0,Accent);t.FontFamily=new("Georgia");t.MaxHeight=32*c.ReadingPercent/100.0;return t;}
    private TextBlock Meaning(DictionaryEntry e,DesktopCard c,int lines=3)
    {var t=Text(e.Meaning,(c.Size=="Small"?11:15)*c.TranslationPercent/100.0);t.MaxHeight=lines*(c.Size=="Small"?15:19)*c.TranslationPercent/100.0;return t;}
    private UIElement Headword(DictionaryEntry e,DesktopCard c,double size,double maxHeight)
    {
        var word=Text(e.Text,size*c.HeadwordPercent/100.0);word.FontFamily=(FontFamily)FindResource("Japanese");word.TextWrapping=TextWrapping.NoWrap;word.TextTrimming=TextTrimming.None;
        return new Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,MaxHeight=maxHeight,Child=word};
    }
    private static void Add(Grid parent,UIElement child,int row=0,int col=0){Grid.SetRow(child,row);Grid.SetColumn(child,col);parent.Children.Add(child);}
    private UIElement Body(DictionaryEntry e,DesktopCard c)
    {
        double scale=c.HeadwordPercent/100.0;var body=new Grid();
        if(c.Size!="Small"&&e.IsKanji)
        {
            body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
            if(c.Size=="Large"&&c.ShowTranslation)body.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var top=new Grid();top.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Min(110,92*scale))});top.ColumnDefinitions.Add(new(){Width=new GridLength(1)});top.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            Add(top,Headword(e,c,c.Size=="Large"?72:54,c.Size=="Large"?140:83));
            Add(top,new Border{Width=1,Height=48,Background=new SolidColorBrush(Color.FromArgb(77,181,61,38)),VerticalAlignment=VerticalAlignment.Center},col:1);
            var definitions=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,0,0)};
            if(c.ShowReading){definitions.Children.Add(Reading(e,c));definitions.Children.Add(new Border{Height=7});}
            if(c.ShowTranslation)definitions.Children.Add(Meaning(e,c,c.TranslationPercent>100?2:3));Add(top,definitions,col:2);Add(body,top);
            if(c.Size=="Large"&&c.ShowTranslation)
            {var details=new StackPanel();details.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromArgb(50,181,61,38)),Margin=new Thickness(0,8,0,8)});var t=Text(e.Detail,14*c.TranslationPercent/100.0);t.MaxHeight=110;details.Children.Add(t);Add(body,details,1);}
        }
        else
        {
            body.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
            body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});
            double max=c.Size=="Small"?65:c.Size=="Medium"?65:155;
            Add(body,Headword(e,c,c.Size=="Small"?(e.IsKanji?36:27):c.Size=="Medium"?54:72,max));
            if(c.ShowReading){var r=Reading(e,c);r.TextWrapping=c.Size=="Large"?TextWrapping.Wrap:TextWrapping.NoWrap;r.MaxHeight=c.Size=="Small"?16*c.ReadingPercent/100.0:32*c.ReadingPercent/100.0;r.Margin=new Thickness(0,0,0,3);Add(body,r,1);}
            if(c.ShowTranslation)Add(body,Meaning(e,c,c.Size=="Small"?2:c.Size=="Medium"?2:5),2);
        }
        return body;
    }
    public void Refresh()
    {
        var c=Configuration;var dimensions=DesktopCard.Dimensions(c.Size);Width=dimensions.Width;Height=dimensions.Height;Topmost=c.Topmost;
        string? error=null,learningError=null;
        try {entry=manager.Store.Display(CardId);}catch(Exception ex){entry=null;error=ex.Message;}
        c=Configuration;displayedId=c.EntryId;token=c.Token;
        bool saved=false;
        if(entry!=null)try{saved=manager.Store.IsSaved(entry.Id);}catch(Exception ex){learningError=ex.Message;}
        EntryBody.Children.Clear();
        if(entry!=null)EntryBody.Children.Add(Body(entry,c));
        else {var message=Text(error??"No entries in this list. Right-click → Edit widget to choose a list or content.",c.Size=="Small"?11:13,Muted);message.VerticalAlignment=VerticalAlignment.Center;EntryBody.Children.Add(message);}
        EntryKind.Text=entry==null?"":entry.IsKanji?"漢字":"言葉";
        FooterLabel.Text=UiText.T(c.Size=="Small"?"":entry==null?"":entry.IsKanji?$"{entry.Strokes} strokes":"JAPANESE · RU");
        SaveButton.IsEnabled=entry!=null;SaveButton.Content=UiText.T(saved?"✓ Saved":learningError!=null?"Retry":"Save");
        SaveButton.ToolTip=UiText.T(actionError??learningError??"Save this entry to Inbox");
        Surface.ToolTip=actionError==null&&error==null?null:UiText.T(actionError??error);
        NextButton.IsEnabled=entry!=null&&error==null;EntryButton.ToolTip=UiText.T(entry==null?"Open My Lists":"Open this entry in Kotoba");
        AutomationProperties.SetName(EntryButton,entry==null?UiText.T("Open My Lists"):$"Открыть запись {entry.Text}");
        lastPoll=LearningStore.Now;
    }
    internal void Poll(double now)
    {
        if(now<retryAt)return;
        try{bool advanced=manager.Store.Tick(CardId,now);if(advanced||now-lastPoll>=15)Refresh();}
        catch(Exception ex){actionError=ex.Message;retryAt=now+15;Refresh();}
    }
    private void Run(Action action)
    {try{action();actionError=null;retryAt=0;manager.LearningChanged();}catch(Exception ex){actionError=ex.Message;}Refresh();}
    internal void SaveDisplayed()=>Run(()=>{if(entry!=null)manager.Store.Save(entry.Id);});
    internal void Advance()=>Run(()=>manager.Store.Advance(CardId,displayedId,token??""));
    private void SaveClick(object sender,RoutedEventArgs e)=>SaveDisplayed();
    private void NextClick(object sender,RoutedEventArgs e)=>Advance();
    private void OpenClick(object sender,RoutedEventArgs e){if(entry!=null)manager.Open(entry.Id);else manager.OpenLists();}
    private void EditClick(object sender,RoutedEventArgs e)=>manager.Edit(CardId);
    private void DragHeaderDown(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source)
        {for(var node=source;node!=null;node=VisualTreeHelper.GetParent(node))if(node is Button)return;}
        if(e.LeftButton==MouseButtonState.Pressed){DragMove();Run(()=>manager.Store.Move(CardId,Left,Top));}
    }
    private ContextMenu Menu()
    {
        var menu=new ContextMenu();
        void Item(string title,Action action,bool? check=null){var item=new MenuItem{Header=UiText.T(title),IsCheckable=check.HasValue,IsChecked=check??false};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Edit widget…",()=>manager.Edit(CardId));
        foreach(var size in new[]{"Small","Medium","Large"}){var target=size;Item(size,()=>Run(()=>manager.Store.Update(Configuration with{Size=target})),Configuration.Size==size);}
        Item("Always on top",()=>Run(()=>manager.Store.Update(Configuration with{Topmost=!Configuration.Topmost})),Configuration.Topmost);
        menu.Items.Add(new Separator());Item("Open Kotoba",()=>manager.Open());Item("Remove widget",()=>manager.Remove(CardId));Item("Quit Kotoba",()=>manager.Quit());return menu;
    }
    internal void ApplyPosition()
    {
        var c=Configuration;var size=DesktopCard.Dimensions(c.Size);
        double x=c.Left??SystemParameters.WorkArea.Right-size.Width-24,y=c.Top??SystemParameters.WorkArea.Top+24+manager.Windows.Count*24;
        // Keep a handle visible even when a previously used monitor is disconnected.
        Left=Math.Clamp(x,SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenLeft+Math.Max(0,SystemParameters.VirtualScreenWidth-48));
        Top=Math.Clamp(y,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenTop+Math.Max(0,SystemParameters.VirtualScreenHeight-48));
    }
}
