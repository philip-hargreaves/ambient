using System.Globalization;

namespace Ambient.App.Core.Common;

/// <summary>Counts, clocks and dates written the same way on every page.</summary>
public static class Words
{
    /// <summary>"1 page", "12 pages".</summary>
    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";

    /// <summary>"m:ss", rounded to the second.</summary>
    public static string Clock(double seconds)
    {
        var whole = (int)Math.Round(seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    /// <summary>"04:37" for a position in the audio: whole seconds, minutes padded.</summary>
    public static string Position(double seconds) => Clock(Math.Floor(seconds)).PadLeft(5, '0');

    /// <summary>A stored timestamp in local time, null when it does not parse.</summary>
    public static DateTimeOffset? LocalTime(string stamp) =>
        DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToLocalTime()
            : null;

    /// <summary>"12 Oct 2020" from any ISO 8601 date, empty from anything else.</summary>
    public static string ShortDate(string stamp) =>
        LocalTime(stamp)?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "";

    /// <summary>"September 2026".</summary>
    public static string Month(DateTimeOffset when) =>
        when.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>"4 Sep" in the given year, "4 Sep 2025" in another; the month in full when asked.</summary>
    public static string Day(DateTimeOffset when, int thisYear, bool fullMonth = false)
    {
        var month = fullMonth ? "MMMM" : "MMM";
        return when.ToString(when.Year == thisYear ? $"d {month}" : $"d {month} yyyy", CultureInfo.CurrentCulture);
    }
}
