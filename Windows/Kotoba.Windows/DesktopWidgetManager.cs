using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kotoba.Core;

namespace Kotoba.Windows;
public sealed class DesktopWidgetManager : IDisposable
{
    private readonly MainWindow main;
    private readonly string path;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Dictionary<string,DesktopWidgetWindow> windows=[];
    private DesktopCardStore? store;
    private TrayIcon? tray;
    private bool disposed;
    private readonly bool offscreen;
    public string? Error {get;private set;}
    public bool TrayAvailable=>tray?.IsVisible==true;
    public IReadOnlyDictionary<string,DesktopWidgetWindow> Windows=>windows;
    public DesktopCardStore Store=>store??throw new InvalidOperationException(Error??"Desktop widget settings are unavailable. Use Retry.");
    public List<DesktopCard> StoreOrEmpty()=>store?.Cards??[];
    public DesktopWidgetManager(MainWindow main,bool enableTray=true,bool offscreen=false)
    {
        this.offscreen=offscreen;
        this.main=main;path=Path.Combine(Path.GetDirectoryName(main.Model.SettingsPath)!,"widgets.json");
        main.DesktopWidgets=this;main.Closing+=MainClosing;
        Restore();
        if(enableTray)try{tray=new TrayIcon(()=>Open(),TrayMenu);if(!tray.IsVisible)Error="The tray icon is unavailable. Use a card's menu to reopen or quit Kotoba.";}catch(Exception ex){Error="Tray is unavailable: "+ex.Message;}
        timer.Tick+=(_,_)=>{foreach(var window in windows.Values.ToArray())window.Poll(LearningStore.Now);};timer.Start();main.Render();
    }
    public void Restore()
    {
        try
        {
            store??=new(path,main.Model.Dictionary,()=>new LearningStore(main.Model.LearningPath,main.Model.Dictionary));
            foreach(var card in store.Cards)if(!windows.ContainsKey(card.Id))Show(card.Id);
            Error=null;
        }
        catch(Exception ex){Error="Could not restore desktop widgets: "+ex.Message;}
    }
    public void Create(string size)
    {
        try
        {
            var card=Store.Create(size);
            if(windows.TryGetValue(card.Id,out var window))
            {if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Refresh();window.Show();if(!offscreen)window.Activate();}
            else Show(card.Id);
            Error=null;
        }
        catch(Exception ex){Error=ex.Message;MessageBox.Show(main,UiText.T(Error),UiText.T("Kotoba desktop widgets"),MessageBoxButton.OK,MessageBoxImage.Error);}
        main.Render();
    }
    private void Show(string id)
    {var window=new DesktopWidgetWindow(this,id);if(offscreen){window.Left=-16000;window.Top=-16000;}windows.Add(id,window);window.Show();}
    public void Remove(string id)
    {
        try
        {
            Store.Remove(id);
            if(windows.Remove(id,out var window)){window.Removing=true;window.Close();}
            main.Render();
        }
        catch(Exception ex){Error=ex.Message;MessageBox.Show(UiText.T(Error),UiText.T("Kotoba desktop widgets"));}
    }
    public void Edit(string id)
    {
        try
        {
            var dialog=new DesktopWidgetSettings(this,id);
            if(main.IsVisible)dialog.Owner=main;
            dialog.ShowDialog();
            if(windows.TryGetValue(id,out var window)){window.Refresh();window.ApplyPosition();}
        }
        catch(Exception ex){MessageBox.Show(UiText.T(ex.Message),UiText.T("Kotoba desktop widgets"));}
    }
    public void Open(string? entryId=null)
    {
        if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;
        main.Show();main.Model.Refresh();
        if(entryId!=null)main.OpenLink("yarxi-study://entry/"+Uri.EscapeDataString(entryId));else main.Model.Notify();
        if(!offscreen)main.Activate();
    }
    public void OpenLists(){main.Model.Page="My Lists";Open();}
    public void LearningChanged(){main.Model.Refresh();if(main.IsVisible)main.Model.Notify();}
    private void MainClosing(object? sender,CancelEventArgs e)
    {
        if(disposed)return;
        if(TrayAvailable||windows.Count>0){e.Cancel=true;main.Hide();}
        else main.Dispatcher.BeginInvoke(()=>Application.Current.Shutdown());
    }
    private ContextMenu TrayMenu()
    {
        var menu=new ContextMenu();
        void Item(string title,Action action){var item=new MenuItem{Header=UiText.T(title)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Open Kotoba",()=>Open());
        foreach(var size in new[]{"Small","Medium","Large"}){var target=size;Item(size+" widget",()=>Create(target));}
        if(windows.Count>0)Item("Show widget",()=>{foreach(var w in windows.Values){if(w.WindowState==WindowState.Minimized)w.WindowState=WindowState.Normal;w.Show();w.Activate();}});
        menu.Items.Add(new Separator());Item("Quit Kotoba",Quit);return menu;
    }
    public void Quit(){Dispose();Application.Current.Shutdown();}
    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();tray?.Dispose();main.Closing-=MainClosing;
        foreach(var window in windows.Values){window.Removing=true;window.Close();}windows.Clear();
        main.DesktopWidgets=null;
    }
}
