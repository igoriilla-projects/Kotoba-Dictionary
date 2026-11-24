using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Kotoba.Core;

namespace Kotoba.Linux;

internal sealed record UserPaths(string Data, string Config)
{
    public string Settings => Path.Combine(Config, "settings.json");
    public string Learning => Path.Combine(Data, "learning.sqlite");
    public string Card => Path.Combine(Data, "widgets.json");

    public static UserPaths ForDesktop()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new(Base("XDG_DATA_HOME", Path.Combine(home, ".local", "share")),
                   Base("XDG_CONFIG_HOME", Path.Combine(home, ".config")));
    }

    private static string Base(string variable, string fallback)
    {
        string? path = Environment.GetEnvironmentVariable(variable);
        return Path.Combine(Path.IsPathFullyQualified(path ?? "") ? path! : fallback, "kotoba");
    }
}

internal static class Program
{
    internal static bool Smoke { get; private set; }
    internal static string InitialCommand { get; private set; } = "";
    internal static string Pipe { get; private set; } = "";
    internal static UserPaths Paths { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        Smoke = args is ["--smoke", _];
        if (Smoke)
        {
            string root = Path.GetFullPath(args[1]);
            Paths = new(Path.Combine(root, "data"), Path.Combine(root, "config"));
            return Run();
        }

        Paths = UserPaths.ForDesktop();
        InitialCommand = args.FirstOrDefault() ?? "";
        string identity = $"{Environment.UserName}:{Paths.Data}";
        Pipe = "kotoba-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        using var mutex = new Mutex(true, Pipe, out bool primary);
        if (primary)
            return Run();
        try
        {
            InstanceChannel.Send(Pipe, InitialCommand).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Не удалось открыть окно Kotoba. {error.Message}");
            return 1;
        }
    }

    private static int Run() => AppBuilder.Configure<App>().UsePlatformDetect()
        .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
        .StartWithClassicDesktopLifetime([], ShutdownMode.OnMainWindowClose);
}
