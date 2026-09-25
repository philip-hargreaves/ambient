using Ambient.App.Core.Hosting;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Shell;

public class EngineStatusInShellTest
{
    [Fact]
    public void TheStatusBarThroughAnEngineLifetime()
    {
        var log = new ListLogger();
        var bar = new StatusBarViewModel(log);

        bar.SetEngineState(EngineStatus.Running);
        Assert.Equal("Starting up", bar.EngineStateLabel);
        Assert.True(bar.EngineStarting);

        bar.SetEngineReady(true);
        Assert.Equal("Ready", bar.EngineStateLabel);
        Assert.False(bar.EngineStarting);

        bar.SetEngineReady(false);
        bar.SetEngineState(EngineStatus.Restarting);
        Assert.Equal("Recovering", bar.EngineStateLabel);

        bar.SetEngineState(EngineStatus.Stopped);
        Assert.Equal("Not running", bar.EngineStateLabel);
        Assert.Empty(log.Lines);  // only faults reach the log

        // Back up: one status, replaced by activity; the ring means in progress
        bar.SetEngineState(EngineStatus.Running);
        bar.SetEngineReady(true);
        Assert.Equal("Ready", bar.DisplayLabel);
        Assert.False(bar.Busy);

        bar.Append("Finalising", busy: true);
        Assert.Equal("Finalising", bar.DisplayLabel);
        Assert.True(bar.Busy);

        bar.Append("Ready for review");
        Assert.Equal("Ready for review", bar.DisplayLabel);
        Assert.False(bar.Busy);

        // Abnormal readiness outranks whatever activity was showing
        bar.SetEngineReady(false);
        bar.SetEngineState(EngineStatus.Restarting);
        Assert.Equal("Recovering", bar.DisplayLabel);
        Assert.True(bar.Busy);

        // Every fault is logged
        var logged = log.Lines.Count;
        bar.SetEngineState(EngineStatus.Faulted);
        Assert.Equal("Recording is unavailable - please restart the app", bar.EngineStateLabel);
        Assert.Equal("Recording is unavailable - please restart the app", bar.LatestActivity);

        bar.SetEngineState(EngineStatus.Faulted);
        Assert.Equal("Recording is unavailable - please restart the app", bar.EngineStateLabel);
        Assert.Equal(logged + 2, log.Lines.Count);
    }

    [Fact]
    public async Task ConsultationActiveTracksTheSessionState()
    {
        var (session, engine, _) = TestSession.Create();
        Assert.False(session.ConsultationActive);

        await session.StartRecordingAsync();
        Assert.True(session.ConsultationActive);

        await session.StopRecordingAsync();
        Assert.True(session.ConsultationActive);

        engine.RaiseNotification("note/ready");
        Assert.True(session.ConsultationActive);

        session.StartNewConsultation();
        Assert.False(session.ConsultationActive);
    }
}
