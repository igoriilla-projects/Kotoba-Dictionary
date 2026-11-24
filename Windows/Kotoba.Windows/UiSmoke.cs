using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using Kotoba.Core;

namespace Kotoba.Windows;
internal static class UiSmoke
{
    public static async Task Run(string[] args)
    {
        string output=Path.GetFullPath(args[Array.IndexOf(args,"--smoke")+1]);Directory.CreateDirectory(output);File.Delete(output+".error");
        string data=Path.Combine(output,"test-data-"+Guid.NewGuid());
        var originalShortcut = new DictionaryShortcut(DictionaryShortcut.Control | DictionaryShortcut.Alt |
                                                      DictionaryShortcut.Shift, 0x86);
        new Settings { DictionaryHotkey = originalShortcut }.Save(Path.Combine(data, "settings.json"));
        var window=new MainWindow(data);
        var m=window.Model; var store=m.Store;
        store.Save("kanji-76");store.Save("word-100");store.Save("word-53797");
        var list=store.CreateList("Everyday Japanese");store.Membership("word-100",list,true);m.Refresh();
        window.ShowInTaskbar=false;window.ShowActivated=false;window.Left=-16000;window.Top=-16000;window.Show();
        var captures=new List<string>();
        async Task Capture(string name)
        {
            m.Refresh();window.Render();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if (name == "search") await WaitForSearch(Descendants(window.Body).OfType<SearchResults>().Single());
            window.UpdateLayout();var root=(FrameworkElement)window.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);captures.Add(name);
        }
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        void Pointer(InteractiveHeadword target,Point point,RoutedEvent routedEvent,MouseButton button=MouseButton.Left)
        {
            // Exercise the registered routed handlers after native TextEditor
            // consumption, without moving the user's actual mouse pointer.
            target.PointerPositionOverride=_=>point;
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,button){RoutedEvent=routedEvent,Handled=true});
        }
        void ClickKanji(InteractiveHeadword target,string character)
        {
            var link=target.Links.First(l=>l.Entry.Text==character);var box=target.Bounds(link);var point=new Point(box.Left+box.Width/2,box.Top+box.Height/2);
            target.Selection.Select(target.Document.ContentStart,target.Document.ContentStart);
            Pointer(target,point,Mouse.PreviewMouseDownEvent);Pointer(target,point,Mouse.MouseUpEvent);target.PointerPositionOverride=null;
        }
        IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
        async Task WaitForSearch(SearchResults results)
        {
            var timeout = Stopwatch.StartNew();
            while (results.ItemsSource is null && timeout.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(15);
            Check(results.ItemsSource is not null, "Search results did not arrive");
            await window.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ApplicationIdle);
        }
        async Task Click(string name)
        {
            var button=Descendants(window).OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)==name);
            Check(button.IsEnabled,"Disabled button: "+name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(m.Error==null,m.Error??"");
        }
        var handle = new WindowInteropHelper(window).Handle;
        async Task VerifyShortcutOpensSearch()
        {
            var keys = (int)((originalShortcut.VirtualKey << 16) | originalShortcut.Modifiers);
            Check(PostMessage(handle, GlobalDictionaryShortcut.HotkeyMessage,
                              GlobalDictionaryShortcut.FirstRegistrationId, keys), "Could not post the shortcut message");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal && m.Page == "Dictionary" &&
                !m.Detail && !m.Exploring, "Shortcut did not restore the window to dictionary search");
            Check(FocusManager.GetFocusedElement(window) is TextBox search &&
                search.SelectionLength == m.Query.Length, "The shortcut did not focus and select the search input");
        }
        m.Page = "Settings";
        m.Query = "旧敵";
        window.WindowState = WindowState.Minimized;
        await VerifyShortcutOpensSearch();
        window.Hide();
        await VerifyShortcutOpensSearch();

        void SelectShortcutKey(string key)
        {
            m.Page = "Settings";
            window.Render();
            window.UpdateLayout();
            Descendants(window.Body).OfType<ComboBox>()
                .Single(picker => AutomationProperties.GetAutomationId(picker) == "DictionaryShortcutKey")
                .SelectedItem = key;
        }
        var newShortcut = originalShortcut with { VirtualKey = 0x87 };
        using (var otherSource = new HwndSource(new HwndSourceParameters("Kotoba shortcut test")))
        using (var otherShortcut = new GlobalDictionaryShortcut(otherSource, () => { }))
        {
            otherShortcut.Change(newShortcut, () => { });
            SelectShortcutKey("F24");
            Descendants(window.Body).OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "Apply shortcut")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(m.Error == "This shortcut is already in use." &&
                Settings.Load(m.SettingsPath).DictionaryHotkey == originalShortcut,
                "A shortcut conflict should leave the previous selection on disk");
            await VerifyShortcutOpensSearch();
            otherShortcut.Change(null, () => { });
            SelectShortcutKey("F24");
            await Click("Apply shortcut");
            Check(Settings.Load(m.SettingsPath).DictionaryHotkey == newShortcut, "Custom shortcut did not persist");
            otherShortcut.Change(originalShortcut, () => { });
            await Click("Disable shortcut");
            Check(Settings.Load(m.SettingsPath).DictionaryHotkey is null, "Disabled shortcut did not persist");
            otherShortcut.Change(newShortcut, () => { });
        }
        m.Page = "Today";
        m.Query = "";
        Check(m.Error==null,m.Error??"");await Capture("today");
        m.Page="My Lists";await Capture("lists");
        m.Page="Dictionary";m.Detail=false;m.Exploring=false;m.Query="あいず";await Capture("search");
        var search = Descendants(window.Body).OfType<TextBox>().Single();
        var searchResults = Descendants(window.Body).OfType<SearchResults>().Single();
        Check(searchResults.Items.Cast<DictionaryEntry>().Any(e => e.Id == "word-100"), "Hiragana search failed");
        search.Text = "きゅうてき";
        await WaitForSearch(searchResults);
        var formerEnemy = searchResults.Items.Cast<DictionaryEntry>().First(e => e.Id == "word-13472");
        m.SelectEntry(formerEnemy);
        window.Render();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Descendants(window.Body).OfType<TextBox>().Any(x => x.Text == "きゅうてき"),
            "The word detail did not render its verified kana");
        await Click("Search");
        search = Descendants(window.Body).OfType<TextBox>().Single();
        searchResults = Descendants(window.Body).OfType<SearchResults>().Single();
        var typing = Stopwatch.StartNew();
        foreach (var prefix in new[] { "с", "си", "сиг", "сигн", "сигна", "сигнал" })
            search.Text = prefix;
        var typingMilliseconds = typing.Elapsed.TotalMilliseconds;
        Check(m.Query == "сигнал" && searchResults.ItemsSource is null, "Search updates ran synchronously during typing");
        await WaitForSearch(searchResults);
        var russianSearchMilliseconds = typing.Elapsed.TotalMilliseconds;
        Check(searchResults.Items.Cast<DictionaryEntry>().Select(e => e.Id).SequenceEqual(m.Dictionary.Search("сигнал").Select(e => e.Id)),
            "An earlier query replaced the latest Russian results");

        search.Text = "п";
        await WaitForSearch(searchResults);
        window.UpdateLayout();
        var panel = Descendants(searchResults).OfType<VirtualizingStackPanel>().Single();
        Check(searchResults.Items.Count == 100 && panel.Children.Count > 0 && panel.Children.Count < 100,
            "Broad Russian search did not virtualize its 100 results");
        var searchScroll = Descendants(searchResults).OfType<ScrollViewer>().Single();
        searchScroll.ScrollToEnd();
        await window.Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var lastResult = searchResults.ItemContainerGenerator.ContainerFromIndex(99);
        Check(lastResult is not null, "The virtualized list cannot reach its last search result");
        var lastEntry = (DictionaryEntry)searchResults.Items[99];
        var resultButton = Descendants(lastResult!).OfType<Button>().Single();
        Check(resultButton.ContextMenu?.Items[0] is MenuItem { Header: CopyHeadwordButton }, "Search result lost its copy menu");
        resultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(m.Detail && m.Entry?.Id == lastEntry.Id, "Search result opened the wrong entry");

        await Click("Search");
        search = Descendants(window.Body).OfType<TextBox>().Single();
        searchResults = Descendants(window.Body).OfType<SearchResults>().Single();
        search.Text = "сигнал";
        window.ContentFilter.SelectedItem = "Words";
        var filteredResults = Descendants(window.Body).OfType<SearchResults>().Single();
        await WaitForSearch(filteredResults);
        Check(filteredResults.Items.Count > 0 && filteredResults.Items.Cast<DictionaryEntry>().All(e => !e.IsKanji)
            && searchResults.ItemsSource is null, "Changing the filter did not cancel the previous search");
        window.ContentFilter.SelectedItem = "Mixed";
        m.SelectEntry(m.Dictionary.Entry("kanji-76")!);await Capture("kanji");
        m.SelectEntry(m.Dictionary.Entry("word-53797")!);await Capture("word");
        m.Preferences.Romaji=true;await Capture("word-romaji");m.Preferences.Romaji=false;
        await Click("Today");await Click("Start Review");Check(m.Reviewing,"Start Review did not enter review");await Capture("review-front");
        await Click("Reveal meaning · Space");Check(m.Session?.RevealedAt!=null,"Reveal failed");await Capture("review-revealed");
        var id=m.Session!.CurrentId!;await Click("Good");Check(m.Summary.RemainingNew==19,"Rating did not consume allowance");
        await Click("Undo latest rating");Check(m.Session!.CurrentId==id&&m.Summary.RemainingNew==20,"Undo did not restore card/quota");
        m.Reviewing=false;m.Page="Settings";await Capture("settings");
        m.Preferences.HeadwordScale=m.Preferences.ReadingScale=m.Preferences.TranslationScale=2;
        m.Page="Today";m.Reviewing=true;await Capture("review-large");
        m.Page="My Lists";await Capture("lists-large");m.Page="Settings";await Capture("settings-large");
        window.Width=816;window.Height=679;m.SelectEntry(m.Dictionary.Entry("word-53797")!);await Capture("minimum-long-word-large");
        m.Page="My Lists";m.Scope=store.CreateList("Empty list");await Capture("empty-list");
        window.OpenLink("yarxi-study://entry/word-100");Check(m.Entry?.Id=="word-100"&&m.Page=="Dictionary","Entry routing failed");
        window.OpenLink("yarxi-study://lists");Check(m.Page=="My Lists","List routing failed");
        window.OpenLink("yarxi-study://setup");Check(m.Page=="Settings","Setup routing failed");
        var backup=m.LearningPath+".backup";File.Move(m.LearningPath,backup);Directory.CreateDirectory(m.LearningPath);
        m.Refresh();Check(m.Error!=null&&m.Learning==null,"Storage failure was hidden");await Capture("storage-error");
        Directory.Delete(m.LearningPath);File.Move(backup,m.LearningPath);m.Refresh();Check(m.Error==null&&m.Saved.Count==3,"Storage recovery lost data");
        m.Preferences.HeadwordScale=m.Preferences.ReadingScale=m.Preferences.TranslationScale=1;
        m.SelectEntry(m.Dictionary.Entry("word-100")!);await Capture("linked-compound");
        var headword=Descendants(window).OfType<InteractiveHeadword>().Single();Check(headword.Links.Count==2,"Compound has missing glyph links");
        var first=headword.Bounds(headword.Links[0]);var second=headword.Bounds(headword.Links[1]);
        Check(!first.IsEmpty&&!second.IsEmpty&&first.Width>0&&second.Left>=first.Right-1,"Actual adjacent glyph rectangles overlap or are empty");
        headword.Hover(new Point(first.Left+first.Width/2,first.Top+first.Height/2));
        Check(((SolidColorBrush)headword.Links[0].Run.Foreground).Color==Color.FromRgb(92,110,117)&&((SolidColorBrush)headword.Links[1].Run.Foreground).Color!=Color.FromRgb(92,110,117),"Hover affected more than one kanji");
        await Snapshot(window,"one-kanji-hover");
        headword.SelectAll();string selection=headword.Selection.Text;headword.Hover(new Point(second.Left+second.Width/2,second.Top+second.Height/2));Check(headword.Selection.Text==selection,"Hover destroyed native text selection");
        headword.Hover(null);
        var repeatedEntry=m.Dictionary.Entry("word-100")! with{Text="日日",Id="preview-repeated"};
        var native=new InteractiveHeadword(repeatedEntry,m.Dictionary,124,_=>{});
        var nativeWindow=new Window{Content=new Grid{Background=(Brush)window.FindResource("Paper"),Children={native}},Width=200,Height=440,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};nativeWindow.Show();
        await native.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nativeWindow.UpdateLayout();
        Check(native.Links.Count==2&&native.Links[0].Run!=native.Links[1].Run,"Repeated kanji share one hover span");
        var repeatedFirst=native.Bounds(native.Links[0]);var repeatedSecond=native.Bounds(native.Links[1]);Check(!repeatedFirst.IsEmpty&&!repeatedSecond.IsEmpty&&repeatedSecond.Top>repeatedFirst.Top,"Large headword did not wrap/hit-test across lines");
        native.Hover(new Point(repeatedFirst.Left+repeatedFirst.Width/2,repeatedFirst.Top+repeatedFirst.Height/2));await Snapshot(nativeWindow,"wrapped-single-kanji-hover");
        string? clicked=null;var mixed=new InteractiveHeadword(repeatedEntry with{Text="食べ物"},m.Dictionary,100,e=>clicked=e.Id);
        nativeWindow.Width=440;nativeWindow.Content=new Grid{Background=(Brush)window.FindResource("Paper"),Children={mixed}};
        await mixed.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);nativeWindow.UpdateLayout();
        var food=mixed.Bounds(mixed.Links[0]);var thing=mixed.Bounds(mixed.Links[1]);
        var kanaPoint=new Point((food.Right+thing.Left)/2,food.Top+food.Height/2);mixed.Hover(kanaPoint);
        Check(mixed.Hit(kanaPoint)==null&&mixed.Cursor==System.Windows.Input.Cursors.IBeam,"Kana incorrectly behaves like a kanji link");
        Check(mixed.Hit(new Point(mixed.ActualWidth-1,1))==null,"Blank area has a false kanji hit");
        ClickKanji(mixed,"物");Check(clicked==m.Dictionary.KanjiIn("物").Single().Id,"Handled native mouse-up did not open the canonical kanji");
        clicked=null;Pointer(mixed,kanaPoint,Mouse.PreviewMouseDownEvent);Pointer(mixed,kanaPoint,Mouse.MouseUpEvent);Check(clicked==null,"Clicking kana opened kanji lookup");
        var dragStart=new Point(food.Left+food.Width/2,food.Top+food.Height/2);Pointer(mixed,dragStart,Mouse.PreviewMouseDownEvent);
        mixed.PointerPositionOverride=_=>kanaPoint;mixed.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent,Handled=true});
        Pointer(mixed,dragStart,Mouse.MouseUpEvent);Check(clicked==null,"A drag returning to its starting kanji opened lookup");mixed.PointerPositionOverride=null;
        mixed.Hover(new Point(food.Left+food.Width/2,food.Top+food.Height/2));await Snapshot(nativeWindow,"mixed-kana-single-hover");nativeWindow.Close();
        m.Preferences.Content="Kanji";m.SelectEntry(m.Dictionary.KanjiIn("図").Single());await Capture("direct-kanji-related-words");
        var related=Descendants(window).OfType<RelatedWordsView>().Single();
        var diagramWords=m.Dictionary.WordsContainingKanji("図");
        Check(related.Words.SequenceEqual(diagramWords)&&related.Words.Any(e=>e.Id=="word-100"),"Direct kanji card ignores related words or applies the Kanji-only filter");
        Check(!Descendants(related.List).OfType<ScrollViewer>().Any()&&double.IsNaN(related.List.Height)&&related.List.ActualHeight>490,"Related words still use a nested/fixed-height viewport");
        var cardScroll=Descendants(window.Body).OfType<ScrollViewer>().Single(s=>s.Content is StackPanel panel&&panel.Children.Contains(related));
        cardScroll.ScrollToVerticalOffset(cardScroll.VerticalOffset+related.TranslatePoint(new Point(0,0),cardScroll).Y);
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        Check(cardScroll.VerticalOffset>0,"Inline related words do not scroll with their card");await Snapshot(window,"inline-related-words-one-scroll");
        var common=new RelatedWordsView(m.Dictionary.Entry("kanji-76")!,m.Dictionary,m.Preferences,false,_=>{});
        var commonScroll=new ScrollViewer{Content=common,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var commonWindow=new Window{Content=commonScroll,Background=(Brush)window.FindResource("Paper"),Width=700,Height=500,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};commonWindow.Show();
        await common.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);commonWindow.UpdateLayout();
        int commonWordCount=m.Dictionary.WordsContainingKanji("一").Count;
        Check(common.List.Items.Count==commonWordCount&&common.List.ItemContainerGenerator.ContainerFromIndex(commonWordCount-1) is ListBoxItem&&!Descendants(common.List).OfType<ScrollViewer>().Any(),"Common-kanji list is capped or contains another scroll host");
        commonScroll.ScrollToEnd();await common.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);commonWindow.UpdateLayout();
        var lastWord=(ListBoxItem)common.List.ItemContainerGenerator.ContainerFromIndex(commonWordCount-1);var lastPoint=lastWord.TranslatePoint(new Point(0,lastWord.ActualHeight/2),commonScroll);
        Check(lastPoint.Y>=0&&lastPoint.Y<commonScroll.ActualHeight,"The single outer scrollbar cannot reach the last related word");await Snapshot(commonWindow,"common-kanji-last-words");commonWindow.Close();
        m.SelectEntry(m.Dictionary.Entry("word-100")!);window.Render();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        var sourceBody=window.Body.Children[0];var original=m.Entry;var reviewSession=m.Store.Session();Exception? lookupFailure=null;int openedLookups=0;
        m.Preferences.Romaji=true;
        window.PrepareLookup=dialog=>{
            openedLookups++;
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-16000;dialog.Top=-16000;dialog.ShowInTaskbar=false;dialog.ShowActivated=false;
            dialog.Dispatcher.BeginInvoke(async ()=>{
                try
                {
                    Check(window.IsLookupOpen,"Lookup does not pause source interaction");await Snapshot(dialog,"kanji-lookup");
                    bool romaji=m.Page!="Today"||!m.Reviewing;
                    Check(Descendants(dialog).OfType<TextBox>().Any(t=>t.Text==dialog.Current.Read(romaji)),"Lookup reading style does not match its Dictionary/Review origin");
                    Check(Descendants(dialog).OfType<RelatedWordsView>().Single().Words.SequenceEqual(diagramWords),"Lookup and inline word lists disagree");
                    dialog.Navigate(m.Dictionary.Entry("word-100")!);Check(dialog.Depth==2&&dialog.Current.Id=="word-100","Nested word navigation failed");await Snapshot(dialog,"lookup-word");
                    dialog.Navigate(m.Dictionary.KanjiIn("合").Single());Check(dialog.Depth==3,"Nested kanji navigation failed");dialog.Back();Check(dialog.Current.Id=="word-100","Back did not restore prior lookup page");
                }
                catch(Exception ex){lookupFailure=ex;}
                finally{dialog.Close();}
            });
        };
        ClickKanji(Descendants(window).OfType<InteractiveHeadword>().Single(),"図");if(lookupFailure!=null)throw lookupFailure;
        Check(openedLookups==1,"Handled mouse click failed to open the dictionary kanji window");
        Check(!window.IsLookupOpen&&ReferenceEquals(window.Body.Children[0],sourceBody)&&m.Entry==original&&m.Store.Session()==reviewSession,"Done changed source card/scroll tree or review session");
        m.Start(scope:list);m.Store.Reveal();m.Refresh();window.Render();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();var reviewBefore=m.Store.Session();
        ClickKanji(Descendants(window).OfType<InteractiveHeadword>().Single(),"図");if(lookupFailure!=null)throw lookupFailure;
        Check(openedLookups==2,"Handled mouse click failed to open the review kanji window");
        Check(m.Store.Session()==reviewBefore&&m.Reviewing,"Lookup altered active review/reveal state");m.Reviewing=false;m.Preferences.Romaji=false;window.PrepareLookup=null;
        string copied="";var feedback=new CopyHeadwordButton("合図",value=>{copied=value;return true;});feedback.ExecuteCopy();Check(copied=="合図"&&feedback.Feedback=="copied","Copy did not acknowledge successful full-word write");
        await Task.Delay(1200);feedback.ExecuteCopy();await Task.Delay(1100);Check(feedback.Feedback=="copied","Repeated copy did not restart feedback duration");await Task.Delay(1100);Check(feedback.Feedback=="ready","Copy feedback did not reset after two seconds");
        var failedCopy=new CopyHeadwordButton("合図",_=>false);failedCopy.ExecuteCopy();Check(failedCopy.Feedback=="failed","Failed copy falsely reported success");
        feedback.ExecuteCopy();
        var copyWindow=new Window{Content=new Border{Background=(Brush)window.FindResource("Paper"),Padding=new Thickness(20),Child=new StackPanel{Children={new CopyHeadwordButton("合図"),feedback,failedCopy}}},Width=340,Height=200,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};copyWindow.Show();await Snapshot(copyWindow,"copy-feedback");copyWindow.Close();
        Check(UiText.T("Today")=="Сегодня"&&UiText.T("Small")=="Малый"&&UiText.T("901 results")=="Найдено: 901","Russian localization missing");
        using(var widgets=new DesktopWidgetManager(window,enableTray:false,offscreen:true))
        {
            async Task CardCapture(DesktopWidgetWindow card,string name)
            {
                card.Refresh();await card.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);card.UpdateLayout();var root=(FrameworkElement)card.Content;
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);captures.Add(name);
            }
            widgets.Create("Medium");var card=widgets.Windows.Values.Single();
            widgets.Store.Update(card.Configuration with{Feed="Learning list",ListId=list,RefreshSeconds=0});
            foreach(var size in new[]{"Small","Medium","Large"})
            {
                widgets.Store.Update(card.Configuration with{Size=size});await CardCapture(card,"widget-"+size.ToLowerInvariant()+"-word");
                widgets.Store.Update(card.Configuration with{Romaji=true,HeadwordPercent=150,ReadingPercent=150,TranslationPercent=150});await CardCapture(card,"widget-"+size.ToLowerInvariant()+"-romaji-large-fonts");
                widgets.Store.Update(card.Configuration with{Romaji=false,HeadwordPercent=100,ReadingPercent=100,TranslationPercent=100});
            }
            var kanjiList=store.CreateList("Kanji widget");store.Membership("kanji-76",kanjiList,true);
            widgets.Store.Update(card.Configuration with{ListId=kanjiList});
            foreach(var size in new[]{"Small","Medium","Large"}){widgets.Store.Update(card.Configuration with{Size=size});await CardCapture(card,"widget-"+size.ToLowerInvariant()+"-kanji");}
            var editor=new DesktopWidgetSettings(widgets,card.CardId){Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};
            Exception? editorFailure=null;
            _=editor.Dispatcher.BeginInvoke(async ()=>{
                try
                {
                    await Snapshot(editor,"widget-settings");
                    var choices=Descendants(editor).OfType<ComboBox>().ToList();choices[0].SelectedItem="Medium";choices[2].SelectedItem="Dictionary";choices[2].SelectedItem="Learning list";
                    Descendants(editor).OfType<Button>().Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception ex){editorFailure=ex;editor.Close();}
            });
            editor.ShowDialog();if(editorFailure!=null)throw editorFailure;
            Check(card.Configuration.Size=="Medium"&&card.Configuration.Feed=="Learning list"&&card.Configuration.Content=="Mixed","Russian option display leaked into persisted widget IDs");
            var previous=card.Configuration;
            widgets.Create("Small");var saving=widgets.Windows.Values.Single();saving.Refresh();
            Check(ReferenceEquals(saving,card)&&widgets.Store.Cards.Count==1&&saving.Configuration==previous with{Size="Small"},"Selecting another size created a second window or changed its selection/settings");
            widgets.Store.Update(saving.Configuration with{Feed="Dictionary"});saving.Refresh();
            string displayed=saving.Configuration.EntryId!;string token=saving.Configuration.Token;
            window.Close();Check(!window.IsVisible&&saving.IsVisible,"Closing main window stopped widgets");
            var saveButton=Descendants(saving).OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)=="Save entry");saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(widgets.Store.IsSaved(displayed)&&saving.Configuration.EntryId==displayed&&saving.Configuration.Token==token,"Widget Save lost entry or failed while app hidden");
            await CardCapture(saving,"widget-saved-main-hidden");
            widgets.Open(displayed);Check(window.IsVisible&&m.Entry?.Id==displayed,"Widget entry did not reopen main window at its exact entry");
            var before=saving.Configuration;var next=Descendants(saving).OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)=="Next entry");next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(saving.Configuration.Token!=before.Token&&saving.Configuration.EntryId!=before.EntryId,"Next button did not advance exactly one dictionary entry");
            string emptyList=store.CreateList("Empty widget");widgets.Store.Update(card.Configuration with{Feed="Learning list",ListId=emptyList});await CardCapture(card,"widget-empty-list");
            widgets.Store.Move(saving.CardId,120,80);var persisted=widgets.Store.Get(saving.CardId);var count=widgets.Windows.Count;widgets.Dispose();
            using var restored=new DesktopWidgetManager(window,enableTray:false,offscreen:true);
            Check(restored.Windows.Count==count&&restored.Store.Get(persisted.Id)==persisted,"Widget manager did not restore instances, positions and current cards");
            m.Page="Settings";await Capture("settings-with-widgets");
        }
        window.Close();File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{Passed=true,Captures=captures,DataDirectory=data,TypingMilliseconds=typingMilliseconds,RussianSearchMilliseconds=russianSearchMilliseconds,Note="Actual WPF window rendered offscreen; not user desktop acceptance."},new JsonSerializerOptions{WriteIndented=true}));
        async Task Snapshot(Window target,string name)
        {
            await target.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);target.UpdateLayout();var root=(FrameworkElement)target.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);if(!captures.Contains(name))captures.Add(name);
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
