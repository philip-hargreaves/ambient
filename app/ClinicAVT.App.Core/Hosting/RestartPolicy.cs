namespace ClinicAVT.App.Core.Hosting;

public enum RecoveryAction
{
    Restart,
    GiveUp,
}

/// <summary>
/// Decides what follows an engine death. The engine restarts up to a crash-storm cutoff, with
/// a growing wait between launches. Mid-consultation the shell resumes the stored session on
/// the new engine, so a restart saves the consultation.
/// </summary>
public static class RestartPolicy
{
    // The same cutoff as the VS Code language client
    public const int StormLimit = 5;

    public static readonly TimeSpan StormWindow = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(8);

    public static RecoveryAction Decide(IReadOnlyList<DateTimeOffset> crashes, DateTimeOffset now)
    {
        var recent = crashes.Count(crash => now - crash <= StormWindow);
        return recent >= StormLimit ? RecoveryAction.GiveUp : RecoveryAction.Restart;
    }

    /// <summary>
    /// The wait before the relaunch. There is none for the first crash in the window, then it
    /// doubles from one second, so a launch that dies at once cannot burn the storm budget in
    /// milliseconds.
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
