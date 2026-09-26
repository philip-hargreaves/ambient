using ClinicAVT.App.Core.Hosting;
using ClinicAVT.App.Platform;
using ClinicAVT.App.Tests.Support;
using ClinicAVT.App.Tests.TestDoubles;
using ClinicAVT.Client;
using static ClinicAVT.App.Tests.Support.Waits;

namespace ClinicAVT.App.Tests.Features.Consultation;

/// <summary>
/// Replay speed must not change the sealed transcript. Compares 16x against
/// 1x on the same recording.
/// </summary>
[Collection("engine")]
[Trait("Requires", "EngineSlow")]  // a full consult at 1x takes about 10 min, so it runs on demand
public class SpeedParityTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task SixteenTimesMatchesRealTime()
    {
        var track = FindTrack();
        if (track is null)
        {
            return;  // demo tracks not staged
        }

        var fast = await ReplayAsync(track, speed: 16, waitSeconds: 45);
        var slow = await ReplayAsync(track, speed: 1, waitSeconds: 560);

        Assert.Equal(slow.Count, fast.Count);
        for (var i = 0; i < slow.Count; i++)
        {
            Assert.Equal(slow[i], fast[i]);
        }
    }

    private static async Task<List<string>> ReplayAsync(string track, double speed, int waitSeconds)
    {
        var pipeName = $"LOCAL\\clinicavt-parity-{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var models = FindModels();
        using var launcher = new ProcessEngineLauncher(
            FindEngine(),
            $"{pipeName} \"{Path.Combine(directory, "store")}\""
                + (models is null ? "" : $" \"{models}\""));
        using var host = new EngineSupervisor(
            launcher, new FakeSession(), TimeProvider.System,
            new FileCrashLog(Path.Combine(directory, "crashes.jsonl")));
        await using var connection = new EngineConnection(host, async (pid, ct) =>
            await PipeTransport.ConnectAsync(pipeName, Timeout, pid, ct));
        try
        {
            host.Start();
            await RetryAsync(() => connection.RequestAsync("engine/echo", new { payload = "up" }, Timeout));
            await connection.RequestAsync(
                "session/start", new { replay = new { path = track, speed, monitor = false } }, Timeout);
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            var stop = await connection.RequestAsync(
                "session/stop", null, TimeSpan.FromSeconds(180));
            var id = stop.GetProperty("sessionId").GetString();
            var transcript = await connection.RequestAsync(
                "session/transcript", new { id }, Timeout);
            return transcript.GetProperty("turns").EnumerateArray()
                .Select(t => $"{t.GetProperty("speaker").GetString()}: {t.GetProperty("text").GetString()}")
                .ToList();
        }
        finally
        {
            host.Shutdown();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? FindTrack()
    {
        var models = FindModels();
        if (models is null)
        {
            return null;
        }

        var track = Path.Combine(
            Path.GetDirectoryName(models)!, "demo", "day2_consultation02_mixed.wav");
        return File.Exists(track) ? track : null;
    }

    private static string? FindModels() => EnginePath.FindModels();

    private static string FindEngine() => EnginePath.Find();
}
