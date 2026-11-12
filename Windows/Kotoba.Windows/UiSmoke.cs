using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Kotoba.Core;

namespace Kotoba.Windows;
internal static class UiSmoke
{
    public static async Task Run(string[] args)
    {
        string output=Path.GetFullPath(args[Array.IndexOf(args,"--smoke")+1]);Directory.CreateDirectory(output);File.Delete(output+".error");
        string data=Path.Combine(output,"test-data-"+Guid.NewGuid());var window=new MainWindow(data);
        var m=window.Model; var store=m.Store;
        store.Save("kanji-76");store.Save("word-100");store.Save("word-53797");
        var list=store.CreateList("Everyday Japanese");store.Membership("word-100",list,true);m.Refresh();
        window.ShowInTaskbar=false;window.ShowActivated=false;window.Left=-16000;window.Top=-16000;window.Show();
        var captures=new List<string>();
        async Task Capture(string name)
        {
            m.Refresh();window.Render();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
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
        async Task Click(string name)
        {
            var button=Descendants(window).OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)==name);
            Check(button.IsEnabled,"Disabled button: "+name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(m.Error==null,m.Error??"");
        }
        Check(m.Error==null,m.Error??"");await Capture("today");
        m.Page="My Lists";await Capture("lists");
        m.Page="Dictionary";m.Detail=false;m.Exploring=false;m.Query="あいず";Check(m.Dictionary.Search(m.Query).Any(e=>e.Id=="word-100"),"Search failed");await Capture("search");
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
        var related=Descendants(window).OfType<RelatedWordsView>().Single();Check(related.Words.Count==84&&related.Words.Any(e=>e.Id=="word-100"),"Direct kanji card ignores related words or applies the Kanji-only filter");
        Check(!Descendants(related.List).OfType<ScrollViewer>().Any()&&double.IsNaN(related.List.Height)&&related.List.ActualHeight>490,"Related words still use a nested/fixed-height viewport");
        var cardScroll=Descendants(window.Body).OfType<ScrollViewer>().Single(s=>s.Content is StackPanel panel&&panel.Children.Contains(related));
        cardScroll.ScrollToVerticalOffset(cardScroll.VerticalOffset+related.TranslatePoint(new Point(0,0),cardScroll).Y);
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);window.UpdateLayout();
        Check(cardScroll.VerticalOffset>0,"Inline related words do not scroll with their card");await Snapshot(window,"inline-related-words-one-scroll");
        var common=new RelatedWordsView(m.Dictionary.Entry("kanji-76")!,m.Dictionary,m.Preferences,false,_=>{});
        var commonScroll=new ScrollViewer{Content=common,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var commonWindow=new Window{Content=commonScroll,Background=(Brush)window.FindResource("Paper"),Width=700,Height=500,Left=-16000,Top=-16000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false};commonWindow.Show();
        await common.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);commonWindow.UpdateLayout();
        Check(common.List.Items.Count==901&&common.List.ItemContainerGenerator.ContainerFromIndex(900) is ListBoxItem&&!Descendants(common.List).OfType<ScrollViewer>().Any(),"Common-kanji list is capped or contains another scroll host");
        commonScroll.ScrollToEnd();await common.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);commonWindow.UpdateLayout();
        var lastWord=(ListBoxItem)common.List.ItemContainerGenerator.ContainerFromIndex(900);var lastPoint=lastWord.TranslatePoint(new Point(0,lastWord.ActualHeight/2),commonScroll);
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
                    Check(Descendants(dialog).OfType<RelatedWordsView>().Single().Words.Count==84,"Lookup and inline word lists disagree");
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
        window.Close();File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{Passed=true,Captures=captures,DataDirectory=data,Note="Actual WPF window rendered offscreen; not user desktop acceptance."},new JsonSerializerOptions{WriteIndented=true}));
        async Task Snapshot(Window target,string name)
        {
            await target.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);target.UpdateLayout();var root=(FrameworkElement)target.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);if(!captures.Contains(name))captures.Add(name);
        }
    }
}
