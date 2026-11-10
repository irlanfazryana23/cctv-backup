using System.Globalization;
using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Backup;

/// <summary>Download speed and time-left estimates, and how to write them for people.</summary>
public static class TransferStats
{
    /// <summary>Too little data early on gives wild numbers, so wait a moment before showing anything.</summary>
    public static readonly TimeSpan MinimumElapsed = TimeSpan.FromSeconds(3);

    /// <summary>Megabytes per second so far, or null while it is too early to say.</summary>
    public static double? SpeedMbPerSec(long sizeKb, double fractionDone, TimeSpan elapsed)
    {
        if (elapsed < MinimumElapsed || fractionDone <= 0 || sizeKb <= 0) return null;
        double mb = sizeKb / 1024.0 * Math.Clamp(fractionDone, 0, 1);
        return mb / elapsed.TotalSeconds;
    }

    /// <summary>Time left at the current pace, or null when unknown.</summary>
    public static TimeSpan? TimeLeft(double remainingMb, double? speedMbPerSec)
    {
        if (speedMbPerSec is not > 0.0001 || remainingMb < 0) return null;
        return TimeSpan.FromSeconds(remainingMb / speedMbPerSec.Value);
    }

    /// <param name="work">Each file: its size in KB and how much of it is done (0 to 1).</param>
    public static (double? Speed, TimeSpan? Left) Overall(IEnumerable<(long SizeKb, double FractionDone)> work, TimeSpan elapsed)
    {
        double doneMb = 0, remainingMb = 0;
        foreach (var (sizeKb, fraction) in work)
        {
            double f = Math.Clamp(fraction, 0, 1);
            doneMb += sizeKb / 1024.0 * f;
            remainingMb += sizeKb / 1024.0 * (1 - f);
        }

        if (elapsed < MinimumElapsed || doneMb <= 0) return (null, null);
        double speed = doneMb / elapsed.TotalSeconds;
        return (speed, TimeLeft(remainingMb, speed));
    }

    public static string FormatSpeed(double? mbPerSec) =>
        mbPerSec is null ? string.Empty : mbPerSec.Value.ToString("0.00", CultureInfo.CurrentCulture) + " MB/s";

    /// <summary>"under 1 min", "12 min", "1 h 05 min", "2 days"...: rounded, because the estimate is rough anyway.</summary>
    public static string FormatDuration(TimeSpan? span)
    {
        if (span is null) return string.Empty;
        var s = span.Value;
        if (s.TotalMinutes < 1) return Loc.T("under 1 min");
        if (s.TotalMinutes < 60) return Loc.F("{0} min", Math.Round(s.TotalMinutes));
        if (s.TotalHours < 48)
        {
            int totalMin = (int)Math.Round(s.TotalMinutes);
            return Loc.F("{0} h {1} min", totalMin / 60, (totalMin % 60).ToString("00"));
        }
        return Loc.F("{0} days", Math.Round(s.TotalDays));
    }
}
