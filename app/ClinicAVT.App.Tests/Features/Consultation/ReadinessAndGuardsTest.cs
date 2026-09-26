using ClinicAVT.App.Core.Features.Consultation;
using ClinicAVT.App.Tests.Support;
using ClinicAVT.App.Tests.TestDoubles;

namespace ClinicAVT.App.Tests.Features.Consultation;

public class ReadinessAndGuardsTest
{
    [Fact]
    public void AStrayNoteHostAndAFailedLanguageListAreBothReportedAtStart()
    {
        var engine = new FakeEngineClient(autoNotify: false)
        {
            StrayNoteHost = true,
            FailNext = method => method == "translate/languages"
                ? new InvalidOperationException("no translator installed")
                : null,
        };

        var log = new ListLogger();
        var (session, _, note) = TestSession.Create(engine: engine, log: log);

        Assert.Contains("stuck in the graphics driver", session.Status.LatestActivity);
        Assert.Contains(log.Lines, line => line.Contains("stray note host"));
        Assert.Empty(note.Languages);
        Assert.Contains(log.Lines, line => line.Contains("translate/languages failed"));
    }

    [Fact]
    public async Task PauseIsIgnoredOutsideARecordingAndWhenNothingChanges()
    {
        var (session, engine, _) = TestSession.Create();

        await session.SetPausedAsync(true);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "session/pause");

        await session.StartRecordingAsync();
        await session.SetPausedAsync(true);
        await session.SetPausedAsync(true);
        Assert.Equal(1, engine.Requests.Count(r => r.Method == "session/pause"));
        Assert.True(session.Paused);

        await session.SetPausedAsync(false);
        Assert.Equal(2, engine.Requests.Count(r => r.Method == "session/pause"));
        Assert.False(session.Paused);
    }

    [Fact]
    public void LosingTheConnectionEndsARunningTranslation()
    {
        var log = new ListLogger();
        var (session, engine, note) = TestSession.Create(log: log);
        note.TranslationRunning = true;

        engine.SetConnected(false);

        Assert.False(note.TranslationRunning);
        Assert.False(session.EngineReady);
        Assert.Contains(log.Lines, line => line.Contains("connection lost"));
    }
}
