using System.Text.Json;
namespace Kotoba.Core;

public sealed class Settings
{
    public string Content { get; set; } = "Mixed";
    public bool Romaji { get; set; }
    public int RefreshSeconds { get; set; } = 3600;
    public int DailyNewLimit { get; set; } = 20;
    public double HeadwordScale { get; set; } = 1;
    public double ReadingScale { get; set; } = 1;
    public double TranslationScale { get; set; } = 1;
    public static string DataDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kotoba");
    public static Settings Load(string path)
    {
        var result = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new Settings();
        result.DailyNewLimit = Math.Clamp(result.DailyNewLimit, 0, 200);
        result.HeadwordScale = Scale(result.HeadwordScale); result.ReadingScale = Scale(result.ReadingScale); result.TranslationScale = Scale(result.TranslationScale);
        if (!new[] {0,300,900,1800,3600,21600,86400}.Contains(result.RefreshSeconds)) result.RefreshSeconds = 3600;
        if (!new[] {"Mixed","Kanji","Words"}.Contains(result.Content)) result.Content = "Mixed";
        return result;
    }
    private static double Scale(double value) => double.IsFinite(value) ? Math.Clamp(value, .5, 2) : 1;
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true);
    }
}
