using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

namespace Ambient.App.Tests.Features.Consultation;

public class ReadinessAndGuardsTest
{
    private static (ConsultationViewModel Session, StatusBarViewModel Status, NoteViewModel Note) Create(
        FakeEngineClient engine)
    {
        var status = new StatusBarViewModel();
        var note = new NoteViewModel();
        var session = new ConsultationViewModel(
            new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), note, status,
            new FakeDialogService(), TestSession.Page(engine, status), TestSession.Guidance(status));
        return (session, status, note);
    }

    [Fact]
    public void AStrayNoteHostIsReportedAtStart()
    {
        var engine = new FakeEngineClient(autoNotify: false) { StrayNoteHost = true };

        var (_, status, _) = Create(engine);

        Assert.Contains("stuck in the graphics driver", status.LatestActivity);
        Assert.Contains(status.LogEntries, line => line.Contains("stray note host"));
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
    public void AFailedLanguageListLeavesTranslationOffAndLogs()
    {
        var engine = new FakeEngineClient(autoNotify: false)
        {
            FailNext = method => method == "translate/languages"
                ? new InvalidOperationException("no translator installed")
                : null,
        };

        var (_, status, note) = Create(engine);

        Assert.Empty(note.Languages);
        Assert.Contains(status.LogEntries, line => line.Contains("translate/languages failed"));
    }

    [Fact]
    public void LosingTheConnectionEndsARunningTranslation()
    {
        var (session, engine, note) = TestSession.Create();
        note.TranslationRunning = true;

        engine.SetConnected(false);

        Assert.False(note.TranslationRunning);
        Assert.False(session.EngineReady);
        Assert.Contains(session.Status.LogEntries, line => line.Contains("connection lost"));
    }
}
