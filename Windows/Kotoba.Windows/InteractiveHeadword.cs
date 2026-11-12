using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kotoba.Core;

namespace Kotoba.Windows;

/// One selectable document. Each composed character has its own hit region;
/// native text selection owns drag gestures and copy, while a click opens lookup.
internal sealed class InteractiveHeadword : RichTextBox
{
    internal sealed record Link(Run Run,DictionaryEntry Entry);
    private readonly List<Link> links=[];
    private readonly Action<DictionaryEntry> open;
    private readonly Brush accent,ink,hoverBrush=new SolidColorBrush(Color.FromRgb(92,110,117));
    private Link? hovered,pressed,flashed;
    private Point down;
    private bool dragging;
    internal Func<MouseEventArgs,Point>? PointerPositionOverride {get;set;}
    private readonly DispatcherTimer flash=new(){Interval=TimeSpan.FromMilliseconds(250)};
    internal IReadOnlyList<Link> Links=>links;
    internal string Headword {get;}
    public InteractiveHeadword(DictionaryEntry entry,DictionaryStore dictionary,double fontSize,Action<DictionaryEntry> open,TextAlignment alignment=TextAlignment.Center)
    {
        this.open=open;Headword=entry.Text;
        accent=(Brush)FindResource("Accent");ink=(Brush)FindResource("Ink");
        IsReadOnly=true;IsReadOnlyCaretVisible=false;BorderThickness=new Thickness(0);Background=Brushes.Transparent;Padding=new Thickness(0);
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
        FontFamily=(FontFamily)FindResource("Japanese");FontSize=fontSize;Foreground=ink;ForceCursor=true;
        var paragraph=new Paragraph{Margin=new Thickness(0),TextAlignment=alignment};
        var map=(entry.IsKanji?[]:dictionary.KanjiIn(entry.Text)).ToDictionary(e=>e.Text);
        var elements=StringInfo.GetTextElementEnumerator(entry.Text);
        while(elements.MoveNext())
        {
            string character=elements.GetTextElement();var run=new Run(character);paragraph.Inlines.Add(run);
            if(map.TryGetValue(character,out var kanji)){run.Foreground=accent;links.Add(new(run,kanji));}
        }
        Document=new FlowDocument(paragraph){PagePadding=new Thickness(0),FontFamily=FontFamily,FontSize=fontSize,TextAlignment=alignment,ColumnWidth=double.PositiveInfinity};
        ToolTip=UiText.T("Select text to copy, or click a kanji to explore it.");
        flash.Tick+=(_,_)=>ClearFlash();Unloaded+=(_,_)=>{Hover(null);ClearFlash();pressed=null;};
        // TextEditor consumes the bubbling mouse events before normal virtual
        // handlers run. Observe that same routed event even when already handled.
        AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(PointerDown),true);
        AddHandler(Mouse.MouseMoveEvent,new MouseEventHandler(PointerMoved),true);
        AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler(PointerUp),true);
        DataObject.AddCopyingHandler(this,(_,e)=>{
            if(e.DataObject.GetData(DataFormats.UnicodeText) is string value&&value==Headword+"\r\n")e.DataObject.SetData(DataFormats.UnicodeText,Headword);
        });
        System.Windows.Automation.AutomationProperties.SetName(this,entry.Text);
    }
    internal Rect Bounds(Link link)
    {
        var start=link.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var end=link.Run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        if(start.IsEmpty||end.IsEmpty)return Rect.Empty;
        return new Rect(Math.Min(start.Left,end.Left),start.Top,Math.Abs(end.Right-start.Left),Math.Max(start.Height,end.Height));
    }
    internal Link? Hit(Point point)=>links.FirstOrDefault(link=>Bounds(link).Contains(point));
    internal void Hover(Point? point)
    {
        var link=point.HasValue?Hit(point.Value):null;
        if(hovered!=null)hovered.Run.Foreground=accent;hovered=link;
        if(link!=null)link.Run.Foreground=hoverBrush;
        Cursor=link==null?Cursors.IBeam:Cursors.Hand;
        ToolTip=link==null?UiText.T("Select text to copy, or click a kanji to explore it."):UiText.T("Open kanji: ")+link.Entry.Text;
    }
    private Point Position(MouseEventArgs e)=>PointerPositionOverride?.Invoke(e)??e.GetPosition(this);
    private void PointerMoved(object sender,MouseEventArgs e)
    {
        var point=Position(e);
        if(pressed!=null&&(Math.Abs(point.X-down.X)>=SystemParameters.MinimumHorizontalDragDistance||Math.Abs(point.Y-down.Y)>=SystemParameters.MinimumVerticalDragDistance))dragging=true;
        Hover(point);
    }
    protected override void OnMouseLeave(MouseEventArgs e){base.OnMouseLeave(e);Hover(null);}
    private void PointerDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return;
        down=Position(e);dragging=false;pressed=e.ClickCount<=1&&Keyboard.Modifiers==ModifierKeys.None?Hit(down):null;
    }
    private void PointerUp(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return;
        var point=Position(e);var target=Hit(point);var candidate=pressed;
        pressed=null;
        if(candidate!=null&&target==candidate&&!dragging&&Selection.IsEmpty&&Math.Abs(point.X-down.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-down.Y)<SystemParameters.MinimumVerticalDragDistance)
            Activate(target);
    }
    internal void Activate(Link link)
    {ClearFlash();flashed=link;link.Run.Background=new SolidColorBrush(Color.FromArgb(46,181,61,38));flash.Start();open(link.Entry);}
    private void ClearFlash(){flash.Stop();if(flashed!=null)flashed.Run.Background=Brushes.Transparent;flashed=null;}
}
