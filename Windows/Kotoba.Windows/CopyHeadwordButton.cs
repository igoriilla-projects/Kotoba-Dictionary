using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kotoba.Windows;
internal sealed class CopyHeadwordButton : Button
{
    private readonly string text;
    private readonly Func<string,bool> copy;
    private readonly DispatcherTimer reset=new(){Interval=TimeSpan.FromSeconds(2)};
    internal string Feedback {get;private set;}="ready";
    public CopyHeadwordButton(string text,Func<string,bool>? copy=null)
    {
        this.text=text;this.copy=copy??WriteClipboard;
        Style=(Style)FindResource(typeof(Button));MinWidth=160;HorizontalAlignment=HorizontalAlignment.Center;
        BorderThickness=new Thickness(0);Padding=new Thickness(8,5,8,5);Background=Brushes.Transparent;Cursor=Cursors.Hand;
        Click+=(_,_)=>ExecuteCopy();reset.Tick+=(_,_)=>Reset();Unloaded+=(_,_)=>Reset();
        MouseEnter+=(_,_)=>Background=new SolidColorBrush(Color.FromArgb(20,181,61,38));MouseLeave+=(_,_)=>Background=Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this,"Copy word / kanji");Update();
    }
    internal static bool WriteClipboard(string value)
    {try{Clipboard.SetText(value);return true;}catch{return false;}}
    internal void ExecuteCopy()
    {reset.Stop();try{Feedback=copy(text)?"copied":"failed";}catch{Feedback="failed";}Update();reset.Start();}
    private void Reset(){reset.Stop();Feedback="ready";Update();}
    private void Update()
    {
        string label=UiText.T(Feedback=="copied"?"Copied":Feedback=="failed"?"Copy failed":"Copy");
        var row=new StackPanel{Orientation=Orientation.Horizontal};
        if(Feedback=="ready")
        {
            var icon=new Canvas{Width=16,Height=16,Margin=new Thickness(0,0,5,0)};
            foreach(var (left,top) in new[]{(5.0,1.0),(2.0,4.0)})
            {var outline=new System.Windows.Shapes.Rectangle{Width=8,Height=10,Stroke=(Brush)FindResource("Ink"),Fill=(Brush)FindResource("Paper"),RadiusX=1,RadiusY=1};Canvas.SetLeft(outline,left);Canvas.SetTop(outline,top);icon.Children.Add(outline);}
            row.Children.Add(icon);
        }
        else row.Children.Add(new TextBlock{Text=Feedback=="copied"?"✓":"!",Width=21,VerticalAlignment=VerticalAlignment.Center});
        row.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});Content=row;
        Foreground=(Brush)FindResource(Feedback=="ready"?"Ink":"Accent");
        ToolTip=UiText.T(Feedback=="copied"?"Copied to clipboard: ":Feedback=="failed"?"Copy failed. Try again.":"Copy entire entry: ")+(Feedback=="failed"?"":text);
        System.Windows.Automation.AutomationProperties.SetName(this,label);
    }
}
