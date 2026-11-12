using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kotoba.Core;

namespace Kotoba.Windows;
public partial class MainWindow : Window
{
    public StudyViewModel Model { get; }
    public DesktopWidgetManager? DesktopWidgets {get;internal set;}
    private readonly DispatcherTimer clock = new() { Interval=TimeSpan.FromSeconds(1) };
    private DateTimeOffset? deadline;
    private TextBlock? countdown;
    private StackPanel? results;
    private bool rendering;
    private KanjiLookupWindow? lookup;
    internal bool IsLookupOpen=>lookup!=null;
    internal Action<KanjiLookupWindow>? PrepareLookup {get;set;}
    private static readonly string[] Pages=["Today","My Lists","Dictionary","Settings"];
    private Brush Ink => (Brush)FindResource("Ink");
    private Brush Accent => (Brush)FindResource("Accent");
    private Brush Muted => (Brush)FindResource("Muted");
    private FontFamily Japanese => (FontFamily)FindResource("Japanese");
    private double H(double size) => size*Model.Preferences.HeadwordScale;
    private double R(double size) => size*Model.Preferences.ReadingScale;
    private double T(double size) => size*Model.Preferences.TranslationScale;
    public MainWindow(string? dataDirectory=null)
    {
        Model=new(dataDirectory); InitializeComponent(); Style=(Style)FindResource(typeof(Window)); DataContext=Model;
        ContentFilter.ItemsSource=new[]{"Mixed","Kanji","Words"}; ContentFilter.SelectedItem=Model.Preferences.Content;
        UiText.Options(ContentFilter);
        Model.PropertyChanged+=(_,_)=>{if(lookup==null)Render();};
        PreviewKeyDown+=KeyDownAction;
        Loaded+=(_,_)=>{
            var client=(FrameworkElement)Content;
            double frameWidth=ActualWidth-client.ActualWidth,frameHeight=ActualHeight-client.ActualHeight;
            MinWidth=800+frameWidth;MinHeight=640+frameHeight;Width=960+frameWidth;Height=700+frameHeight;
        };
        Activated+=(_,_)=>{if(lookup!=null)return;Model.Refresh(); Model.Notify(); ResetTimer();};
        Deactivated+=(_,_)=>{ deadline=null; UpdateCountdown(); };
        Closed+=(_,_)=>clock.Stop();
        clock.Tick+=(_,_)=>Tick(); clock.Start(); Render();
    }
    private TextBlock Text(string value,double size=13,Brush? color=null) => new() {Text=UiText.T(value),FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap};
    private StackPanel Column(params UIElement[] children) { var p=new StackPanel(); foreach(var child in children) p.Children.Add(child); return p; }
    private StackPanel Row(params UIElement[] children) { var p=new StackPanel {Orientation=Orientation.Horizontal}; foreach(var child in children) p.Children.Add(child); return p; }
    private Button Button(string title,Action action,bool primary=false)
    {
        var b=new Button {Content=UiText.T(title),Margin=new Thickness(0,0,8,0),Style=(Style)FindResource(primary?"Primary":typeof(Button)),ToolTip=UiText.T(title),HorizontalAlignment=HorizontalAlignment.Left};
        AutomationProperties.SetName(b,UiText.T(title));AutomationProperties.SetAutomationId(b,title); b.Click+=(_,_)=>Model.Act(action); return b;
    }
    private UIElement Gap(double height=16) => new Border {Height=height};
    private Border Line() => new() {Height=1,Background=new SolidColorBrush(Color.FromArgb(35,41,51,46)),Margin=new Thickness(0,12,0,12)};
    private Border CardPanel(UIElement child,double radius=18) => new() {Background=new SolidColorBrush(Color.FromArgb(148,255,255,255)),CornerRadius=new CornerRadius(radius),Padding=new Thickness(30),Child=child};
    private ScrollViewer Scroll(UIElement child) => new() {Content=child};
    private TextBox Selectable(string text,double size,Brush? color=null,FontFamily? font=null)
    {
        var box=new TextBox {Text=text,IsReadOnly=true,IsReadOnlyCaretVisible=false,TextWrapping=TextWrapping.Wrap,FontSize=size,Foreground=color??Ink,FontFamily=font??FontFamily,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        TextBlock.SetLineHeight(box,size*1.15);TextBlock.SetLineStackingStrategy(box,LineStackingStrategy.BlockLineHeight);
        AutomationProperties.SetName(box,text); return box;
    }
    private CopyHeadwordButton CopyButton(string text)=>new(text);
    private InteractiveHeadword Headword(DictionaryEntry entry,double size,Action<DictionaryEntry>? open=null,TextAlignment alignment=TextAlignment.Center)=>new(entry,Model.Dictionary,size,open??OpenLookup,alignment);
    internal void OpenLookup(DictionaryEntry entry)
    {
        if(lookup!=null){lookup.Navigate(entry);return;}
        lookup=new KanjiLookupWindow(this,entry);PrepareLookup?.Invoke(lookup);ResetTimer();
        try{lookup.ShowDialog();}
        finally
        {
            lookup=null;Model.Refresh();
            foreach(var controls in Descendants(Body).OfType<ContentControl>().Where(c=>c.Tag is DictionaryEntry).ToList())controls.Content=BuildSaveControls((DictionaryEntry)controls.Tag);
            ErrorText.Text=UiText.T(Model.Error);ErrorBanner.Visibility=Model.Error==null?Visibility.Collapsed:Visibility.Visible;ResetTimer();
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    internal UIElement LookupPage(DictionaryEntry entry,Action<DictionaryEntry> open)
    {
        bool romaji=Model.Page=="Today"&&Model.Reviewing?false:Model.Preferences.Romaji;
        var panel=Column(Headword(entry,H(entry.IsKanji?76:46),open,TextAlignment.Left),Gap(14),Selectable(entry.Read(romaji),R(16),Accent),Gap(14),Selectable(entry.Meaning,T(20)));
        if(entry.Detail!=entry.Meaning){panel.Children.Add(Gap(14));panel.Children.Add(Selectable(entry.Detail,T(14),Muted));}
        if(entry.IsKanji){panel.Children.Add(Gap(14));panel.Children.Add(Text($"{entry.Strokes} strokes",12,Muted));}
        panel.Children.Add(Gap(14));panel.Children.Add(Row(CopyButton(entry.Text),SaveControls(entry)));
        if(Model.Error!=null)panel.Children.Add(Text(Model.Error,12,Accent));
        var page=Column(CardPanel(panel,16));
        if(entry.IsKanji)page.Children.Add(new RelatedWordsView(entry,Model.Dictionary,Model.Preferences,romaji,open));
        page.Margin=new Thickness(24);return Scroll(page);
    }
    public void Render()
    {
        if(rendering) return; rendering=true;
        try
        {
            Navigation.Children.Clear(); Body.Children.Clear(); countdown=null;
            foreach(var page in Pages)
            {
                var target=page;
                var b=Button(page,()=>{Model.Page=target; ResetTimer();}); b.HorizontalContentAlignment=HorizontalAlignment.Left;
                b.Content=Row(Text(page switch {"Today"=>"☼", "My Lists"=>"▤","Dictionary"=>"▣",_=>"⚙"},17),new Border {Width=12},Text(page,13,Model.Page==page?Accent:Ink));
                b.Margin=new Thickness(0,0,0,5); b.Padding=new Thickness(12,11,12,11); b.BorderThickness=new Thickness(0);
                b.HorizontalAlignment=HorizontalAlignment.Stretch;
                b.Background=Model.Page==page?new SolidColorBrush(Color.FromArgb(23,181,61,38)):Brushes.Transparent;
                Navigation.Children.Add(b);
            }
            KanjiCount.Text=UiText.T($"{Model.Counts.Kanji:N0} kanji"); WordCount.Text=UiText.T($"{Model.Counts.Words:N0} words");
            Eyebrow.Text=UiText.T(Model.Page=="Today"?"少しずつ · LITTLE BY LITTLE":"JAPANESE–RUSSIAN");
            PageTitle.Text=UiText.T(Model.Page=="Today"?"A little practice, today.":Model.Page);
            ContentFilter.Visibility=Model.Page=="Dictionary"?Visibility.Visible:Visibility.Collapsed;
            ErrorBanner.Visibility=Model.Error==null?Visibility.Collapsed:Visibility.Visible; ErrorText.Text=UiText.T(Model.Error);
            Body.Children.Add(Model.Page switch {"Today"=>Today(),"My Lists"=>Lists(),"Dictionary"=>Dictionary(),_=>SettingsView()});
            UpdateCountdown();
        }
        finally {rendering=false;}
    }
    private UIElement Today()
    {
        var grid=new Grid(); grid.RowDefinitions.Add(new(){Height=GridLength.Auto}); grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)}); grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var metrics=Row(Metric(Model.Summary.Due,"due reviews"),new Border{Width=36},Metric(Model.Summary.RemainingNew,"new entries remaining today")); metrics.Margin=new Thickness(0,0,0,24); grid.Children.Add(metrics);
        UIElement content; UIElement? footer=null;
        if(!Model.Reviewing)
        {
            content=Column(Text("Save entries from the dictionary. Review them here, a little each day.",13,Muted),Gap(24),Button(Model.Session?.CurrentId==null?"Start Review":"Continue Review",()=>{Model.Start(Model.Session?.CurrentId!=null);ResetTimer();},true),Gap(16),Text($"{Model.Summary.AvailableNew} saved entries waiting to be learned · due reviews come first",12,Muted));
        }
        else if(Model.ReviewEntry is DictionaryEntry entry)
        {
            var word=Headword(entry,H(entry.IsKanji?100:62));
            var reading=Selectable(entry.Read(),R(15),Accent); reading.TextAlignment=TextAlignment.Center;
            var copy=CopyButton(entry.Text);
            var body=Column(word,Gap(20),copy,Gap(20),reading,Gap(8),Line());
            if(Model.Session?.RevealedAt is double at)
            {
                var meaning=Selectable(entry.Meaning,T(17)); meaning.TextAlignment=TextAlignment.Center; body.Children.Add(meaning);body.Children.Add(Gap(20));
                var grades=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
                foreach(var grade in new[]{ReviewGrade.Again,ReviewGrade.Good,ReviewGrade.Easy})
                {
                    var g=grade; var b=Button(g.ToString(),()=>Model.Rate(g)); b.MinWidth=90;
                    var label=Column(Text(g.ToString(),13,Accent),Gap(5),Text(ReviewScheduler.IntervalText(Model.CurrentCard,g,at),11,Accent));
                    foreach(TextBlock text in label.Children.OfType<TextBlock>()) text.TextAlignment=TextAlignment.Center;
                    b.Content=label; grades.Children.Add(b);
                }
                body.Children.Add(grades);body.Children.Add(Gap(18)); var hint=Text("1 Again · 2 Good · 3 Easy",11,Muted);hint.TextAlignment=TextAlignment.Center;body.Children.Add(hint);
            }
            else {var reveal=Button("Reveal meaning · Space",()=>Model.Store.Reveal(),true);reveal.HorizontalAlignment=HorizontalAlignment.Center;body.Children.Add(reveal);}
            content=CardPanel(Scroll(body)); footer=ReviewFooter();
        }
        else
        {
            content=Column(Text("You're caught up for now.",24),Gap(),Text("Come back when reviews are due, or save more entries for a future day.",13,Muted),Gap(24),Button("Check for due reviews",()=>Model.Start(true)),Gap(),Button("Done",()=>Model.Reviewing=false)); footer=ReviewFooter();
        }
        Grid.SetRow(content,1);grid.Children.Add(content);
        if(footer!=null){Grid.SetRow(footer,2);grid.Children.Add(footer);}return grid;
    }
    private UIElement Metric(int number,string caption)
    {var n=Text(number.ToString(),36);n.FontFamily=new("Georgia");return Column(n,Gap(4),Text(caption,12,Muted));}
    private UIElement ReviewFooter()
    {
        var dock=new DockPanel {Margin=new Thickness(0,20,0,0)};
        var pause=Button("Pause review",()=>{Model.Reviewing=false;ResetTimer();});DockPanel.SetDock(pause,Dock.Right);dock.Children.Add(pause);
        var undo=Button("Undo latest rating",()=>Model.Store.Undo());undo.IsEnabled=Model.Learning?.CanUndo()==true;undo.HorizontalAlignment=HorizontalAlignment.Left; dock.Children.Add(undo);return dock;
    }
    private UIElement Dictionary()
    {
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var tabs=Row(Button("Search",()=>{Model.Exploring=false;Model.Detail=false;ResetTimer();}),Button("Explore",()=>{Model.Exploring=true;Model.Detail=false;Model.TranslationVisible=true;ResetTimer();}));tabs.Margin=new Thickness(0,0,0,18);grid.Children.Add(tabs);
        UIElement content=Model.Exploring||Model.Detail?EntryCard():Search();Grid.SetRow(content,1);grid.Children.Add(content);return grid;
    }
    private UIElement Search()
    {
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var search=new TextBox {Text=Model.Query,FontSize=15,Tag=UiText.T("Search Japanese, hiragana, rōmaji, or Russian…"),ToolTip=UiText.T("Search Japanese, hiragana, rōmaji, or Russian…"),Margin=new Thickness(0,0,0,14)};
        AutomationProperties.SetName(search,UiText.T("Search Japanese, hiragana, rōmaji, or Russian"));search.TextChanged+=(_,_)=>{Model.Query=search.Text;UpdateSearch();};grid.Children.Add(search);
        results=new StackPanel();var scroll=Scroll(results);Grid.SetRow(scroll,1);grid.Children.Add(scroll);UpdateSearch();return grid;
    }
    private void UpdateSearch()
    {
        if(results==null)return; results.Children.Clear();
        try
        {
            var entries=Model.Dictionary.Search(Model.Query,Model.Kind);results.Children.Add(Text($"{entries.Count} results{(entries.Count==100?" · first 100 shown":"")}",12,Muted));results.Children.Add(Gap(12));
            if(entries.Count==0) {results.Children.Add(Empty("No results","Try another Japanese, hiragana, rōmaji, or Russian search."));return;}
            foreach(var entry in entries) {results.Children.Add(EntryRow(entry,false));results.Children.Add(Line());}
        }
        catch(Exception ex){results.Children.Add(Text("Could not read dictionary: "+ex.Message,13,Accent));}
    }
    private UIElement EntryRow(DictionaryEntry entry,bool saved)
    {
        var grid=new Grid{Margin=new Thickness(0,8,0,8)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});if(saved)grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var line=new Grid();line.ColumnDefinitions.Add(new(){Width=new GridLength(110)});line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var word=Text(entry.Text,H(26));word.FontFamily=Japanese;line.Children.Add(word);
        var detail=Column(Text(entry.Read(),R(12),Accent),Gap(5),Text(entry.Meaning,T(12)));Grid.SetColumn(detail,1);line.Children.Add(detail);
        var b=Button(entry.Text,()=>{Model.SelectEntry(entry);ResetTimer();});b.Content=line;b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);b.Padding=new Thickness(8);
        var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header=CopyButton(entry.Text),StaysOpenOnClick=true});b.ContextMenu=menu;grid.Children.Add(b);
        if(saved){var controls=SaveControls(entry);Grid.SetColumn(controls,1);grid.Children.Add(controls);}return grid;
    }
    private UIElement EntryCard()
    {
        if(Model.Entry is not DictionaryEntry entry)return Empty("No entry available","Try another content filter.");
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var meta=new DockPanel();var strokes=Text(entry.IsKanji?$"{entry.Strokes} strokes":"",11,Muted);DockPanel.SetDock(strokes,Dock.Right);meta.Children.Add(strokes);var kind=Text(entry.IsKanji?"KANJI / 漢字":"WORD / 言葉",10,Accent);kind.FontFamily=new("Consolas");meta.Children.Add(kind);
        var word=Headword(entry,H(entry.IsKanji?110:68));word.MinHeight=125;
        var copy=CopyButton(entry.Text);
        var reading=Selectable(entry.Read(Model.Preferences.Romaji),R(19),Accent,new("Georgia"));reading.TextAlignment=TextAlignment.Center;
        var panel=Column(meta,Gap(24),word,Gap(18),copy,Gap(18),reading,Line());
        if(Model.TranslationVisible){panel.Children.Add(Selectable(entry.Meaning,T(23)));if(entry.IsKanji&&entry.Detail!=entry.Meaning){panel.Children.Add(Gap(18));panel.Children.Add(Selectable(entry.Detail,T(14),Muted));}if(entry.IsKanji)panel.Children.Add(new RelatedWordsView(entry,Model.Dictionary,Model.Preferences,Model.Preferences.Romaji,OpenLookup));}
        else panel.Children.Add(Button("Reveal translation",()=>Model.TranslationVisible=true));
        grid.Children.Add(CardPanel(Scroll(panel)));
        var footer=new DockPanel{Margin=new Thickness(0,18,0,0)};var next=Button("Next entry  →",()=>{Model.Next();ResetTimer();},true);DockPanel.SetDock(next,Dock.Right);footer.Children.Add(next);
        var actions=Row(SaveControls(entry),Button(Model.TranslationVisible?"Hide translation":"Show translation",()=>Model.TranslationVisible=!Model.TranslationVisible));footer.Children.Add(actions);Grid.SetRow(footer,1);grid.Children.Add(footer);
        var bottom=new DockPanel{Margin=new Thickness(0,18,0,0)};countdown=Text("",10,Muted);DockPanel.SetDock(countdown,Dock.Right);bottom.Children.Add(countdown);bottom.Children.Add(Text(Model.Preferences.Romaji?"Rōmaji readings · Russian translations":"Hiragana readings · Russian translations",10,Muted));Grid.SetRow(bottom,2);grid.Children.Add(bottom);return grid;
    }
    private UIElement SaveControls(DictionaryEntry entry)
        =>new ContentControl{Tag=entry,Content=BuildSaveControls(entry)};
    private UIElement BuildSaveControls(DictionaryEntry entry)
    {
        var record=Model.Saved.FirstOrDefault(r=>r.Id==entry.Id);
        if(record==null)return Button("Save to Inbox",()=>Model.Store.Save(entry.Id));
        var context=new ContextMenu();
        if(Model.Lists.Count==0)context.Items.Add(new MenuItem{Header=UiText.T("Create a list in My Lists"),IsEnabled=false});
        foreach(var list in Model.Lists)
        {var target=list;var item=new MenuItem{Header=list.Name,IsCheckable=true,IsChecked=record.Lists.Contains(list.Id)};item.Click+=(_,_)=>Model.Act(()=>Model.Store.Membership(entry.Id,target.Id,item.IsChecked));context.Items.Add(item);}
        context.Items.Add(new Separator());var remove=new MenuItem{Header=UiText.T("Remove from saved entries")};remove.Click+=(_,_)=>Model.Act(()=>Model.Store.Remove(entry.Id));context.Items.Add(remove);
        var organize=new Button {Content=UiText.T("Organize"),ContextMenu=context,Margin=new Thickness(8,0,8,0)};
        organize.Click+=(_,_)=>{context.PlacementTarget=organize;context.IsOpen=true;};
        return Row(Text("✓ Saved",13),organize);
    }
    private UIElement Lists()
    {
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var options=new List<LearningList>{new("inbox",UiText.T("Inbox")),new("all",UiText.T("All saved"))};options.AddRange(Model.Lists);
        if(!options.Any(o=>o.Id==(Model.Scope??"all")))Model.Scope="inbox";
        var picker=new ComboBox{ItemsSource=options,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=Model.Scope??"all",MinWidth=170,Margin=new Thickness(0,0,12,0)};
        picker.SelectionChanged+=(_,_)=>{if(!rendering)Model.Act(()=>Model.Scope=picker.SelectedValue as string);};
        var tools=Row(picker,Button("New list",()=>Rename(null)));
        if(Model.Scope is string scope && scope!="inbox"&&scope!="all") {tools.Children.Add(Button("Rename",()=>Rename(scope)));tools.Children.Add(Button("Delete list",()=>{Model.Store.DeleteList(scope);Model.Scope="inbox";}));}
        var search=new TextBox{Text=Model.SavedQuery,Tag=UiText.T("Search saved entries…"),ToolTip=UiText.T("Search saved entries…"),Margin=new Thickness(0,16,12,0),MinWidth=200};AutomationProperties.SetName(search,UiText.T("Search saved entries"));
        var kind=new ComboBox{ItemsSource=new[]{"Mixed","Kanji","Words"},SelectedItem=Model.SavedKind,Width=110,Margin=new Thickness(0,16,0,0)};
        UiText.Options(kind);
        var filterGrid=new Grid();filterGrid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});filterGrid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filterGrid.Children.Add(search);Grid.SetColumn(kind,1);filterGrid.Children.Add(kind);
        var heading=Column(tools,filterGrid,Gap(16));grid.Children.Add(heading);
        var listRows=new StackPanel();var listScroll=Scroll(listRows);Grid.SetRow(listScroll,1);grid.Children.Add(listScroll);
        void Update()
        {
            listRows.Children.Clear();string query=DictionaryStore.Fold(Model.SavedQuery);
            var entries=Model.Saved.Where(r=>Model.Scope=="all" || (Model.Scope=="inbox"?r.Lists.Count==0:r.Lists.Contains(Model.Scope!))).Select(r=>Model.Dictionary.Entry(r.Id)).OfType<DictionaryEntry>()
                .Where(e=>(Model.SavedKind=="Mixed"||(Model.SavedKind=="Kanji"?e.IsKanji:!e.IsKanji))&&new[]{e.Text,e.Hiragana,e.Reading,e.Meaning}.Any(s=>DictionaryStore.Fold(s).Contains(query,StringComparison.Ordinal))).ToList();
            var info=new DockPanel();var review=Button("Review this list",()=>{Model.Start(scope:Model.Scope=="all"?null:Model.Scope);ResetTimer();});DockPanel.SetDock(review,Dock.Right);info.Children.Add(review);info.Children.Add(Text($"{entries.Count} entries",12,Muted));listRows.Children.Add(info);listRows.Children.Add(Gap());
            if(entries.Count==0)listRows.Children.Add(Empty("No saved entries here","Save entries in Dictionary. Use Organize on an entry to assign lists."));
            else foreach(var entry in entries){listRows.Children.Add(EntryRow(entry,true));listRows.Children.Add(Line());}
        }
        search.TextChanged+=(_,_)=>{Model.SavedQuery=search.Text;Update();};kind.SelectionChanged+=(_,_)=>{Model.SavedKind=(string)kind.SelectedItem;Update();};Update();return grid;
    }
    private void Rename(string? id)
    {
        var dialog=new Window{Title=UiText.T(id==null?"New list":"Rename list"),Owner=this,Width=380,Height=185,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var input=new TextBox{Text=Model.Lists.FirstOrDefault(l=>l.Id==id)?.Name??""};
        var save=new Button{Content=UiText.T("Save"),IsDefault=true,Style=(Style)FindResource("Primary"),Margin=new Thickness(0,0,8,0)};var cancel=new Button{Content=UiText.T("Cancel"),IsCancel=true};
        save.Click+=(_,_)=>{try {if(id==null)Model.Scope=Model.Store.CreateList(input.Text);else Model.Store.RenameList(id,input.Text);dialog.DialogResult=true;}catch(Exception ex){MessageBox.Show(dialog,UiText.T(ex.Message),"Kotoba");}};
        dialog.Content=new Border{Padding=new Thickness(20),Child=Column(input,Gap(18),Row(save,cancel))};dialog.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};dialog.ShowDialog();
    }
    private UIElement Empty(string title,string description) => CardPanel(Column(Text(title,22,Muted),Gap(12),Text(description,13,Muted)),12);
    private UIElement SettingsView()
    {
        var panel=new StackPanel();
        if(DesktopWidgets is DesktopWidgetManager widgets)
        {
            var controls=Column(Text("Choose a size to show or resize your desktop card. Right-click it to change its source, fonts or timer. Drag its KOTOBA header to move it.",13,Muted),Gap(),
                Row(Button("Small",()=>widgets.Create("Small")),Button("Medium",()=>widgets.Create("Medium")),Button("Large",()=>widgets.Create("Large"))),Gap(12),
                Text("Closing the main window keeps the card running. Use the Kotoba tray menu to reopen it or quit.",12,Muted));
            if(widgets.Error!=null){controls.Children.Add(Text(widgets.Error,12,Accent));controls.Children.Add(Button("Retry widgets",()=>{widgets.Restore();}));}
            foreach(var card in widgets.StoreOrEmpty())
            {var id=card.Id;controls.Children.Add(Gap(10));controls.Children.Add(Row(Text(card.Size+" · "+card.Feed),new Border{Width=12},Button("Edit",()=>widgets.Edit(id)),Button("Remove",()=>widgets.Remove(id))));}
            controls.Children.Add(Gap());controls.Children.Add(Button("Quit Kotoba",widgets.Quit));panel.Children.Add(Section("Desktop widget",controls));
        }
        var sizes=new StackPanel();
        var preview=Column();
        void Preview(){preview.Children.Clear();preview.Children.Add(Text("日本語",H(30)));((TextBlock)preview.Children[0]).FontFamily=Japanese;preview.Children.Add(Text("にほんご",R(16),Accent));preview.Children.Add(Text("японский язык",T(16)));}
        void SizeControl(string title,Func<double> get,Action<double> set)
        {
            var label=Text($"{get():P0}",13,Muted);var heading=new DockPanel();DockPanel.SetDock(label,Dock.Right);heading.Children.Add(label);heading.Children.Add(Text(title));
            var slider=new Slider{Minimum=.5,Maximum=2,TickFrequency=.05,IsSnapToTickEnabled=true,Value=get(),Margin=new Thickness(0,8,0,18)};
            AutomationProperties.SetName(slider,UiText.T(title+" size"));
            slider.ValueChanged+=(_,_)=>{set(slider.Value);label.Text=$"{slider.Value:P0}";Preview();try{Model.PersistSettings();}catch(Exception ex){ErrorText.Text=ex.Message;ErrorBanner.Visibility=Visibility.Visible;}};
            sizes.Children.Add(heading);sizes.Children.Add(slider);
        }
        SizeControl("Word / kanji",()=>Model.Preferences.HeadwordScale,v=>Model.Preferences.HeadwordScale=v);
        SizeControl("Reading",()=>Model.Preferences.ReadingScale,v=>Model.Preferences.ReadingScale=v);
        SizeControl("Translation",()=>Model.Preferences.TranslationScale,v=>Model.Preferences.TranslationScale=v);
        Preview();sizes.Children.Add(preview);sizes.Children.Add(Gap());sizes.Children.Add(Button("Reset text sizes",()=>{Model.Preferences.HeadwordScale=Model.Preferences.ReadingScale=Model.Preferences.TranslationScale=1;Model.PersistSettings();}));
        panel.Children.Add(Section("Text size",sizes));
        var limit=Text($"New entries per day: {Model.Preferences.DailyNewLimit}");
        var less=Button("−",()=>{Model.Preferences.DailyNewLimit=Math.Max(0,Model.Preferences.DailyNewLimit-1);Model.PersistSettings();});
        var more=Button("+",()=>{Model.Preferences.DailyNewLimit=Math.Min(200,Model.Preferences.DailyNewLimit+1);Model.PersistSettings();});
        panel.Children.Add(Section("Learning",Column(Row(limit,new Border{Width=20},less,more),Gap(12),Text("Shared across every list, plus all due reviews. First rating counts; saving and browsing do not. Review fronts always use hiragana.",12,Muted))));
        var reading=new ComboBox{ItemsSource=new[]{"Hiragana","Rōmaji"},SelectedIndex=Model.Preferences.Romaji?1:0,Width=180,HorizontalAlignment=HorizontalAlignment.Left};UiText.Options(reading);reading.SelectionChanged+=(_,_)=>Model.Act(()=>{Model.Preferences.Romaji=reading.SelectedIndex==1;Model.PersistSettings();});
        var intervals=new[]{("Off",0),("Every 5 minutes",300),("Every 15 minutes",900),("Every 30 minutes",1800),("Every hour",3600),("Every 6 hours",21600),("Every day",86400)};
        var timer=new ComboBox{ItemsSource=intervals.Select(i=>i.Item1),SelectedIndex=Array.FindIndex(intervals,i=>i.Item2==Model.Preferences.RefreshSeconds),Width=180,HorizontalAlignment=HorizontalAlignment.Left};UiText.Options(timer);timer.SelectionChanged+=(_,_)=>Model.Act(()=>{Model.Preferences.RefreshSeconds=intervals[timer.SelectedIndex].Item2;Model.PersistSettings();ResetTimer();});
        panel.Children.Add(Section("Explore",Column(Text("Reading format"),Gap(8),reading,Gap(),Text("Refresh timer"),Gap(8),timer,Gap(12),Text("Automatically show another random card while Dictionary → Explore is open and the app is active. Choosing Off keeps each card until you press Next.",12,Muted))));
        panel.Children.Add(Section("Hiragana readings",Text("Readings are transcribed from the dictionary’s rōmaji. Long vowels use ー where the source does not specify their kana spelling. Rōmaji remains available above.",13,Muted)));
        panel.Children.Add(Section("Kotoba for Windows",Column(Text("Version 1.4.1 · Offline Japanese–Russian dictionary",12,Muted),Gap(8),Text("Your lists and review progress are stored locally on this PC.",12,Muted))));return Scroll(panel);
    }
    private UIElement Section(string title,UIElement body)
    {var heading=Text(title,14);heading.FontWeight=FontWeights.SemiBold;var card=CardPanel(Column(heading,Gap(16),body),12);card.Padding=new Thickness(20);card.Margin=new Thickness(0,0,0,18);return card;}
    private void ContentChanged(object sender,SelectionChangedEventArgs e)
    {if(rendering||ContentFilter.SelectedItem is not string content||Model.Preferences.Content==content)return;Model.Act(()=>{Model.Preferences.Content=content;Model.PersistSettings();Model.Next();ResetTimer();});}
    private void Retry(object sender,RoutedEventArgs e)=>Model.Act(()=>{Model.ReloadDictionary();Model.Refresh();});
    private bool TimerEnabled => IsActive && lookup==null&&Model.Page=="Dictionary"&&Model.Exploring&&!Model.Reviewing&&Model.Preferences.RefreshSeconds>0;
    public void ResetTimer(){deadline=TimerEnabled?DateTimeOffset.UtcNow.AddSeconds(Model.Preferences.RefreshSeconds):null;UpdateCountdown();}
    private void UpdateCountdown(){if(countdown!=null)countdown.Text=UiText.T(deadline is DateTimeOffset due?$"Next card in {Math.Max(0,(int)(due-DateTimeOffset.UtcNow).TotalMinutes):00}:{Math.Max(0,(int)(due-DateTimeOffset.UtcNow).TotalSeconds)%60:00}":"Auto-refresh off");}
    private void Tick()
    {
        if(!TimerEnabled)deadline=null;
        else if(deadline==null)ResetTimer();
        else if(DateTimeOffset.UtcNow>=deadline){Model.Act(()=>{Model.Next();ResetTimer();});}
        if(DateTimeOffset.UtcNow.Second%15==0&&IsActive&&Model.Page=="Today"&&!Model.Reviewing) {Model.Refresh();Model.Notify();}
        UpdateCountdown();
    }
    private void KeyDownAction(object sender,KeyEventArgs e)
    {
        if(e.Handled||lookup!=null)return;
        if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.OemComma){Model.Act(()=>Model.Page="Settings");e.Handled=true;return;}
        if(Keyboard.FocusedElement is TextBoxBase {IsReadOnly:false}||Keyboard.FocusedElement is ComboBox)return;
        if(Model.Page=="Today"&&Model.Reviewing)
        {
            if(e.Key==Key.Z&&Keyboard.Modifiers==ModifierKeys.Control&&Model.Learning?.CanUndo()==true){Model.Act(()=>Model.Store.Undo());e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.None&&e.Key==Key.Space&&Model.Session?.RevealedAt==null){Model.Act(()=>Model.Store.Reveal());e.Handled=true;}
            else if(Keyboard.Modifiers==ModifierKeys.None&&Model.Session?.RevealedAt!=null&&e.Key is Key.D1 or Key.D2 or Key.D3 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3)
            {var grade=e.Key is Key.D1 or Key.NumPad1?ReviewGrade.Again:e.Key is Key.D2 or Key.NumPad2?ReviewGrade.Good:ReviewGrade.Easy;Model.Act(()=>Model.Rate(grade));e.Handled=true;}
        }
        else if(Model.Page=="Dictionary"&&(Model.Exploring||Model.Detail)&&Keyboard.Modifiers==ModifierKeys.None&&e.Key==Key.Space){Model.Act(()=>{Model.Next();ResetTimer();});e.Handled=true;}
    }
    public void OpenLink(string? value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="yarxi-study")return;
        lookup?.Close();lookup=null;
        Model.Act(()=>{switch(uri.Host){case "entry": var entry=Model.Dictionary.Entry(Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')));if(entry!=null)Model.SelectEntry(entry);break;case "lists":Model.Page="My Lists";break;case "setup":Model.Page="Settings";break;}ResetTimer();});
    }
}
