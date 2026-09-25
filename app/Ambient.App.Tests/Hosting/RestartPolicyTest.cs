using Ambient.App.Core.Hosting;

namespace Ambient.App.Tests.Hosting;

public class RestartPolicyTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    private static DateTimeOffset[] CrashesAt(params TimeSpan[] agos) =>
        [.. agos.Select(ago => Now - ago)];

    private static DateTimeOffset[] RecentCrashes(int count) =>
        CrashesAt(Enumerable.Repeat(TimeSpan.FromSeconds(10), count).ToArray());

    [Fact]
    public void TheStormLimitWithinTheWindowIsTheLineAndRelaunchesBackOff()
    {
        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide([], Now));
        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide(RecentCrashes(1), Now));
        Assert.Equal(RecoveryAction.Restart,
            RestartPolicy.Decide(RecentCrashes(RestartPolicy.StormLimit - 1), Now));
        Assert.Equal(RecoveryAction.GiveUp,
            RestartPolicy.Decide(RecentCrashes(RestartPolicy.StormLimit), Now));

        // Crashes outside the window do not count
        var agos = Enumerable
            .Repeat(RestartPolicy.StormWindow + TimeSpan.FromSeconds(1), RestartPolicy.StormLimit - 1)
            .Append(TimeSpan.Zero)
            .ToArray();
        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide(CrashesAt(agos), Now));

        // The first relaunch is immediate and the next ones wait longer
        Assert.Equal(TimeSpan.Zero, RestartPolicy.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(1), RestartPolicy.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(2), RestartPolicy.Backoff(3));
        Assert.Equal(TimeSpan.FromSeconds(4), RestartPolicy.Backoff(4));
        Assert.Equal(RestartPolicy.MaxBackoff, RestartPolicy.Backoff(9));
    }
}
