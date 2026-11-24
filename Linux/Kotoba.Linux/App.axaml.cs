using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kotoba.Core;

namespace Kotoba.Linux;

internal sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(Program.Paths);
            desktop.MainWindow = window;
            window.Opened += OnFirstOpen;
            if (!Program.Smoke)
            {
                var stop = new CancellationTokenSource();
                desktop.Exit += (_, _) => stop.Cancel();
                _ = Listen(window, stop.Token);
            }

            async void OnFirstOpen(object? sender, EventArgs args)
            {
                window.Opened -= OnFirstOpen;
                if (!Program.Smoke)
                {
                    AddTrayIcon(window);
                    window.Route(Program.InitialCommand);
                    return;
                }
                try { await Smoke.Run(window); desktop.Shutdown(0); }
                catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void AddTrayIcon(MainWindow window)
    {
        var mark = Ui.Text("字", 24, Brushes.White);
        mark.FontFamily = Ui.Japanese;
        mark.TextAlignment = TextAlignment.Center;
        var face = new Border { Background = Ui.Accent, Child = mark };
        face.Measure(new Size(32, 32));
        face.Arrange(new Rect(0, 0, 32, 32));
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
        bitmap.Render(face);
        using var png = new MemoryStream();
        bitmap.Save(png);
        png.Position = 0;
        var icon = new TrayIcon { Icon = new WindowIcon(png), ToolTipText = "Kotoba", Menu = new NativeMenu() };
        Add("Открыть Kotoba", () => window.Route(""));
        Add("Поиск по словарю", () => window.Route("--search"));
        Add("Настройки карточки", () => window.Route("yarxi-study://setup"));
        Add("Выйти из Kotoba", window.Quit);
        icon.Clicked += (_, _) => window.Route("");
        TrayIcon.SetIcons(this, new TrayIcons { icon });

        void Add(string label, Action action)
        {
            var menuItem = new NativeMenuItem(label);
            menuItem.Click += (_, _) => Dispatcher.UIThread.Post(action);
            icon.Menu.Items.Add(menuItem);
        }
    }

    private static async Task Listen(MainWindow window, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var command = await InstanceChannel.Receive(Program.Pipe, stop);
                if (command is not null)
                    await Dispatcher.UIThread.InvokeAsync(() => window.Route(command));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (IOException error)
            {
                Console.Error.WriteLine(error.Message);
                await Task.Delay(150);
            }
        }
    }
}
