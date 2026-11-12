using System.IO;
using System.Security.Principal;
using System.Windows;
using Kotoba.Core;
using System.Globalization;
using System.Windows.Markup;

namespace Kotoba.Windows;
public partial class App : Application
{
    private Mutex? instance;
    private readonly CancellationTokenSource stop = new();
    private string pipe = "";
    private DesktopWidgetManager? widgets;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture=CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("ru-RU");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        DispatcherUnhandledException += (_,args) => { MessageBox.Show(UiText.T(args.Exception.Message),"Kotoba",MessageBoxButton.OK,MessageBoxImage.Error); args.Handled=true; };
        if(e.Args.Contains("--smoke")) { try { await UiSmoke.Run(e.Args); Shutdown(0); } catch(Exception ex) { File.WriteAllText(e.Args[Array.IndexOf(e.Args,"--smoke")+1]+".error",ex.ToString()); Shutdown(1); } return; }
        pipe = "Kotoba-" + WindowsIdentity.GetCurrent().User!.Value.Replace('-','_');
        instance = new Mutex(true,"Local\\"+pipe,out bool first);
        if(!first)
        {
            try { await InstanceChannel.Send(pipe,e.Args.FirstOrDefault()??"",allowForeground:true); }
            catch(Exception ex) { MessageBox.Show(UiText.T("Could not contact the running Kotoba window. " + ex.Message),"Kotoba"); }
            Shutdown(); return;
        }
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new MainWindow(); MainWindow=window; window.Show();window.OpenLink(e.Args.FirstOrDefault());
        widgets=new DesktopWidgetManager(window);
        _=Listen(window);
    }
    private async Task Listen(MainWindow window)
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var line=await InstanceChannel.Receive(pipe,stop.Token);
                await Dispatcher.InvokeAsync(()=> { window.OpenLink(line); if(window.WindowState==WindowState.Minimized) window.WindowState=WindowState.Normal; window.Show(); window.Activate(); });
            }
            catch(OperationCanceledException) { break; }
            catch(IOException) { }
        }
    }
    protected override void OnExit(ExitEventArgs e) { stop.Cancel();widgets?.Dispose(); instance?.Dispose(); base.OnExit(e); }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e){widgets?.Dispose();base.OnSessionEnding(e);}
}
