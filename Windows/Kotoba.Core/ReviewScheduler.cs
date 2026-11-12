// Mechanical C# adaptation of Vendor/SwiftFSRS v5.0.0, MIT. See PROVENANCE.md.
namespace Kotoba.Core;

public enum ReviewGrade { Again = 1, Hard = 2, Good = 3, Easy = 4 }
public sealed record Card(double Due = 0, double Stability = 0, double Difficulty = 0, double ElapsedDays = 0,
    double ScheduledDays = 0, int Reps = 0, int Lapses = 0, int State = 0, double? LastReview = null);
public sealed record ReviewLog(int Rating, int State, double Due, double Stability, double Difficulty,
    double ElapsedDays, double LastElapsedDays, double ScheduledDays, double Review);
public sealed record ScheduleResult(Card Card, ReviewLog Log);

public static class ReviewScheduler
{
    private static readonly double[] W = [0.4072,1.1829,3.1262,15.4722,7.2102,0.5316,1.0651,0.0234,1.616,0.1544,1.0824,1.9813,0.0953,0.2975,2.2042,0.2407,2.9466,0.5034,0.6567];
    private static double Fixed(double x) => Math.Round(x, 8, MidpointRounding.ToEven);
    private static double Clamp(double x) => Fixed(Math.Clamp(x, .01, 36500));
    private static double Constrain(double x) => Math.Clamp(Fixed(x), 1, 10);
    private static double InitD(int g) => Constrain(W[4] - Math.Exp((g - 1) * W[5]) + 1);
    private static double NextD(double d, int g) => Constrain(Fixed(W[7] * InitD(4) + (1 - W[7]) * (d - W[6] * (g - 3))));
    private static double Short(double s, int g) => Clamp(s * Math.Exp(W[17] * (g - 3 + W[18])));
    private static int Interval(double s) => (int)Math.Clamp(Math.Round(s, MidpointRounding.AwayFromZero), 1, 36500);
    private static double Recall(double d, double s, double r, int g) => Clamp(s * (1 + Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp((1 - r) * W[10]) - 1) * (g == 2 ? W[15] : 1) * (g == 4 ? W[16] : 1)));
    public static ScheduleResult Next(Card? card, ReviewGrade grade, double now)
    {
        int g = (int)grade;
        if (g < 1 || g > 4) throw new ArgumentOutOfRangeException(nameof(grade));
        var last = card ?? new Card(Due: now);
        double elapsed = last.State != 0 && last.LastReview.HasValue ? Math.Floor((now - last.LastReview.Value) / 86400) : 0;
        var current = last with { LastReview = now, ElapsedDays = elapsed, Reps = last.Reps + 1 };
        var next = current; double minutes = 0; int days = 0;
        if (last.State == 0)
        {
            next = next with { Difficulty = InitD(g), Stability = Math.Max(W[g - 1], .1), State = g == 4 ? 2 : 1 };
            if (g == 4) days = Interval(next.Stability); else minutes = g == 1 ? 1 : g == 2 ? 5 : 10;
        }
        else if (last.State is 1 or 3)
        {
            next = next with { Difficulty = NextD(last.Difficulty, g), Stability = Short(last.Stability, g), State = g <= 2 ? last.State : 2 };
            if (g <= 2) minutes = g == 1 ? 5 : 10;
            else days = g == 3 ? Interval(next.Stability) : Math.Max(Interval(next.Stability), Interval(Short(last.Stability, 3)) + 1);
        }
        else
        {
            double r = Fixed(Math.Pow(1 + (19.0 / 81 * elapsed) / last.Stability, -.5));
            double s = g == 1 ? Clamp(W[11] * Math.Pow(last.Difficulty, -W[12]) * (Math.Pow(last.Stability + 1, W[13]) - 1) * Math.Exp((1 - r) * W[14])) : Recall(last.Difficulty, last.Stability, r, g);
            next = next with { Difficulty = NextD(last.Difficulty, g), Stability = s, State = g == 1 ? 3 : 2, Lapses = last.Lapses + (g == 1 ? 1 : 0) };
            int hard = Math.Min(Interval(Recall(last.Difficulty, last.Stability, r, 2)), Interval(Recall(last.Difficulty, last.Stability, r, 3)));
            int good = Math.Max(Interval(Recall(last.Difficulty, last.Stability, r, 3)), hard + 1);
            if (g == 1) minutes = 5;
            else days = g == 2 ? hard : g == 3 ? good : Math.Max(Interval(s), good + 1);
        }
        next = next with { ScheduledDays = days, Due = now + days * 86400.0 + minutes * 60 };
        return new(next, new(g, current.State, last.LastReview ?? last.Due, current.Stability, current.Difficulty, elapsed, last.ElapsedDays, current.ScheduledDays, now));
    }
    public static string IntervalText(Card? card, ReviewGrade grade, double now)
    {
        double seconds = Math.Max(0, Next(card, grade, now).Card.Due - now);
        return seconds < 3600 ? $"{Math.Max(1, (int)(seconds / 60))} min" : seconds < 86400 ? $"{(int)(seconds / 3600)} hr" : $"{Math.Round(seconds / 86400, MidpointRounding.AwayFromZero)} days";
    }
}
