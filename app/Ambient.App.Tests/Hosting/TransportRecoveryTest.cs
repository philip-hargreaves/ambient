using System.Diagnostics;
using Ambient.App.Core.Hosting;
using Ambient.App.Platform;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;
using static Ambient.App.Tests.Support.Waits;

namespace Ambient.App.Tests.Hosting;

/// <summary>
/// The whole shell stack against the real engine: supervisor, launcher and
/// connection together. A kill mid-idle must heal without a new client.
/// </summary>
[Collection("engine")]
[Trait("Requires", "Engine")]
public class TransportRecoveryTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static string FindEngine() => EnginePath.Find();

    [Fact]
    public async Task TheConnectionSurvivesASupervisedRestart()
    {
        var pipeName = $"LOCAL\\ambient-e2e-{Guid.NewGuid():N}";
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var crashPath = Path.Combine(directory, "crashes.jsonl");
        using var launcher = new ProcessEngineLauncher(FindEngine(), pipeName);
        using var host = new EngineSupervisor(
            launcher, new FakeSession(), TimeProvider.System, new FileCrashLog(crashPath));
        await using var connection = new EngineConnection(host, async (pid, ct) =>
            await PipeTransport.ConnectAsync(pipeName, Timeout, pid, ct));
        try
        {
            // Echo tests the transport alone and needs no microphone on a
            // CI runner
            host.Start();
            await RetryAsync(() => connection.RequestAsync("engine/echo", new { payload = "up" }, Timeout));
            var firstPid = host.EnginePid;
            Assert.NotNull(firstPid);

            Process.GetProcessById(firstPid.Value).Kill();

            await RetryAsync(() => connection.RequestAsync("engine/echo", new { payload = "back" }, Timeout));
            Assert.NotEqual(firstPid, host.EnginePid);
            Assert.Single(File.ReadAllLines(crashPath));

            var patientReady = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connection.NotificationReceived += (method, _) =>
            {
                if (method == "patient/ready")
                {
                    patientReady.TrySetResult();
                }
            };
            await connection.RequestAsync("session/stop", null, Timeout);
            await patientReady.Task.WaitAsync(Timeout);
        }
        finally
        {
            host.Shutdown();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
