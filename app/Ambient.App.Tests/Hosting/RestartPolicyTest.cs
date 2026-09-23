using Ambient.App.Core.Hosting;

namespace Ambient.App.Tests.Hosting;

public class RestartPolicyTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddHours(1);

    private static DateTimeOffset[] CrashesAt(params TimeSpan[] agos) =>
        [.. agos.Select(ago => Now - ago)];

    [Fact]
    public void MidConsultationRestartsSoTheSessionCanResume()
    {
        var storm = CrashesAt(Enumerable.Repeat(TimeSpan.Zero, 10).ToArray());

        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide([], Now));
        Assert.Equal(RecoveryAction.GiveUp, RestartPolicy.Decide(storm, Now));
    }

    [Fact]
    public void IdleCrashRestarts()
    {
        var crashes = CrashesAt(TimeSpan.Zero);

        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide(crashes, Now));
    }

    [Fact]
    public void ReachingTheStormLimitGivesUp()
    {
        var crashes = CrashesAt(
            Enumerable.Repeat(TimeSpan.FromSeconds(10), RestartPolicy.StormLimit).ToArray());

        Assert.Equal(RecoveryAction.GiveUp, RestartPolicy.Decide(crashes, Now));
    }

    [Fact]
    public void JustUnderTheStormLimitRestarts()
    {
        var crashes = CrashesAt(
            Enumerable.Repeat(TimeSpan.FromSeconds(10), RestartPolicy.StormLimit - 1).ToArray());

        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide(crashes, Now));
    }

    [Fact]
    public void CrashesOutsideTheWindowDoNotCount()
    {
        var agos = Enumerable
            .Repeat(RestartPolicy.StormWindow + TimeSpan.FromSeconds(1), RestartPolicy.StormLimit - 1)
            .Append(TimeSpan.Zero)
            .ToArray();

        Assert.Equal(RecoveryAction.Restart, RestartPolicy.Decide(CrashesAt(agos), Now));
    }

    [Fact]
    public void TheFirstRelaunchIsImmediateAndTheNextOnesWaitLonger()
    {
        Assert.Equal(TimeSpan.Zero, RestartPolicy.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(1), RestartPolicy.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(2), RestartPolicy.Backoff(3));
        Assert.Equal(TimeSpan.FromSeconds(4), RestartPolicy.Backoff(4));
        Assert.Equal(RestartPolicy.MaxBackoff, RestartPolicy.Backoff(9));
    }
}
