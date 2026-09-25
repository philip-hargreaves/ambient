namespace Ambient.App.Core.Hosting;

public enum RecoveryAction
{
    Restart,
    GiveUp,
}

/// <summary>
/// The decision on an engine death: restart, bounded by a crash-storm cutoff and spaced
/// by a growing wait. Mid-consultation the shell resumes the stored session on the new
/// engine, so a restart saves the consultation.
/// </summary>
public static class RestartPolicy
{
    // VS Code's language-client cutoff
    public const int StormLimit = 5;

    public static readonly TimeSpan StormWindow = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);

    public static RecoveryAction Decide(IReadOnlyList<DateTimeOffset> crashes, DateTimeOffset now)
    {
        var recent = crashes.Count(crash => now - crash <= StormWindow);
        return recent >= StormLimit ? RecoveryAction.GiveUp : RecoveryAction.Restart;
    }

    /// <summary>
    /// The wait before the relaunch: none for the first crash in the window, then doubling
    /// from one second, so a launch that dies at once cannot burn the storm budget in ms.
    /// </summary>
    public static TimeSpan Backoff(int recentCrashes)
    {
        if (recentCrashes <= 1)
        {
            return TimeSpan.Zero;
        }

        var seconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, recentCrashes - 2));
        return TimeSpan.FromSeconds(seconds);
    }
}
