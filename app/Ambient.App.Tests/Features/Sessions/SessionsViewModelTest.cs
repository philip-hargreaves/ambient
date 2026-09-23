using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;
using static Ambient.App.Tests.Support.Waits;

namespace Ambient.App.Tests.Features.Sessions;

public class SessionsViewModelTest
{
    private static (SessionsViewModel Sessions, ConsultationViewModel Consultation,
        FakeEngineClient Engine, StatusBarViewModel Status) Create()
    {
        var engine = new FakeEngineClient(autoNotify: false);
        var note = new NoteViewModel();
        var status = new StatusBarViewModel();
        var consultation = new ConsultationViewModel(
            new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), note, status,
            new FakeDialogService(), TestSession.Page(engine, status), TestSession.Guidance(status));
        return (new SessionsViewModel(new EngineApi(engine), status, consultation, new FakeDialogService(), new RecordingNavigationService()), consultation, engine, status);
    }

    private static void ScriptOneSession(FakeEngineClient engine)
    {
        engine.Responses["session/list"] = new
        {
            sessions = new[]
            {
                new
                {
                    id = "abc",
                    startedAt = "2026-08-17T10:15:00Z",
                    endedAt = "2026-08-17T10:23:41Z",
                    state = "finalised",
                    sampleRate = 16000,
                    label = "Elbow swelling",
                    editedAt = "2026-08-17T10:31:00Z",
                    audioSeconds = 542.0,  // a 16x replay: wall clock says 8:41, the audio 9 min
                },
            },
        };
        engine.Responses["session/transcript"] = new
        {
            turns = new[]
            {
                new { firstFrame = 480000L, frameCount = 48000L, speaker = "doctor", text = "hello" },
            },
        };
        engine.Responses["session/note"] = new
        {
            text = "the note",
            style = "soap",
            detail = "concise",
            generatedAt = "2026-08-17T10:24:00Z",
            editedAt = "2026-08-17T10:31:00Z",
        };
        engine.Responses["session/patient"] = new
        {
            text = "the sheet",
            language = "en",
            editedAt = (string?)null,
            translation = new { language = "pl", text = "arkusz" },
        };
    }

    [Fact]
    public async Task RefreshListsSessionsWithLabelAndEditStamp()
    {
        var (vm, _, engine, _) = Create();
        ScriptOneSession(engine);

        await vm.RefreshAsync();

        var row = Assert.Single(vm.Sessions);
        Assert.Equal("abc", row.Id);
        Assert.Equal("Elbow swelling", row.Title);
        Assert.Equal("9 min", row.Duration);
        Assert.True(row.Edited);
        Assert.StartsWith("Edited ", row.EditedLabel);
        Assert.True(row.HasLabel);
        Assert.Equal("Elbow swelling", row.Heading);
        Assert.Equal($"{row.Started} · 9 min", row.Meta);
        Assert.True(row.MetaVisible);
    }

    [Fact]
    public async Task AMissingLabelFallsBackToTheDateAndTime()
    {
        var (vm, _, engine, _) = Create();
        engine.Responses["session/list"] = new
        {
            sessions = new[]
            {
                new
                {
                    id = "abc",
                    startedAt = "2026-08-17T10:15:00Z",
                    endedAt = "2026-08-17T10:23:41Z",
                    state = "finalised",
                    sampleRate = 16000,
                    label = "",
                    editedAt = (string?)null,
                    demo = true,
                    hasReflection = true,
                },
            },
        };

        await vm.RefreshAsync();

        var row = Assert.Single(vm.Sessions);
        Assert.Equal(row.Started, row.Title);
        Assert.False(row.Edited);
        Assert.True(row.Demo);
        Assert.True(row.HasReflection);
        Assert.False(row.HasLabel);
        Assert.Equal($"{row.Started} · {row.Duration}", row.Heading);  // said once
        Assert.False(row.MetaVisible);
    }

    [Fact]
    public async Task SelectingOpensTheSessionIntoTheSharedPanes()
    {
        var (vm, consultation, engine, _) = Create();
        ScriptOneSession(engine);
        await vm.RefreshAsync();

        vm.Selected = vm.Sessions[0];
        await WaitUntilAsync(() => vm.DetailOpen);

        Assert.True(vm.DetailOpen);
        Assert.Contains(engine.Requests, c => c.Method == "session/open" && c.Params.Contains("abc"));
        Assert.Equal(SessionState.Review, consultation.State);
        Assert.Equal("the note", consultation.Note.ClinicalNoteText);
        Assert.Equal("the sheet", consultation.Note.PatientInfoText);
        Assert.Equal("arkusz", consultation.Note.TranslationText);
        Assert.Equal("pl", consultation.Note.TranslationLanguage);
        Assert.Equal("pl translation", consultation.Note.TranslationCaption);
        Assert.Equal("soap", consultation.Note.Style);
        Assert.True(consultation.Note.Edited);
        Assert.Single(consultation.Transcript.Turns);
        Assert.Equal("Elbow swelling", vm.DetailTitle);
        Assert.Contains("SOAP, concise", vm.DetailMeta);
    }

    [Fact]
    public async Task AnOpenTheEngineRefusesLeavesTheDetailClosed()
    {
        var (vm, consultation, engine, _) = Create();
        ScriptOneSession(engine);
        engine.Failing.Add("session/open");
        await vm.RefreshAsync();

        vm.Selected = vm.Sessions[0];
        await WaitUntilAsync(() => engine.Requests.Any(r => r.Method == "session/open"));

        Assert.False(vm.DetailOpen);
        Assert.Equal(SessionState.Idle, consultation.State);
    }

    [Fact]
    public async Task RenamingSendsTheLabelAndUpdatesTheRow()
    {
        var (vm, _, engine, _) = Create();
        ScriptOneSession(engine);
        await vm.RefreshAsync();
        vm.Selected = vm.Sessions[0];
        await WaitUntilAsync(() => vm.DetailOpen);

        vm.DetailTitle = "Left elbow bursitis";
        await vm.RenameAsync();

        Assert.Contains(engine.Requests, c => c.Method == "session/label"
            && c.Params.Contains("Left elbow bursitis"));
        Assert.Equal("Left elbow bursitis", vm.Sessions[0].Title);
        Assert.Same(vm.Sessions[0], vm.Selected);
        Assert.True(vm.DetailOpen, "renaming must not close the open session");
        Assert.Equal(1, engine.Requests.Count(c => c.Method == "session/open"));
    }

    [Fact]
    public async Task LeavingClosesTheReviewAndSavesEdits()
    {
        var (vm, consultation, engine, _) = Create();
        ScriptOneSession(engine);
        await vm.RefreshAsync();
        vm.Selected = vm.Sessions[0];
        await WaitUntilAsync(() => vm.DetailOpen);

        consultation.Note.ClinicalNoteText = "the note, corrected";
        await vm.LeaveAsync();

        Assert.Contains(engine.Requests, c => c.Method == "note/update"
            && c.Params.Contains("the note, corrected"));
        Assert.Contains(engine.Requests, c => c.Method == "session/close");
        Assert.Equal(SessionState.Idle, consultation.State);
        Assert.False(vm.DetailOpen);
    }

    [Fact]
    public async Task DeleteClosesTheReviewFirstAndRefreshes()
    {
        var (vm, _, engine, _) = Create();
        ScriptOneSession(engine);
        await vm.RefreshAsync();
        vm.Selected = vm.Sessions[0];
        await WaitUntilAsync(() => vm.DetailOpen);

        await vm.DeleteCommand.ExecuteAsync(vm.Selected);

        var close = engine.Requests.FindIndex(c => c.Method == "session/close");
        var delete = engine.Requests.FindIndex(c => c.Method == "session/delete");
        Assert.True(close >= 0 && delete > close, "close precedes delete");
        Assert.Equal(2, engine.Requests.Count(c => c.Method == "session/list"));
    }

    [Fact]
    public async Task EngineErrorsLandInTheStatusLogNotAsCrashes()
    {
        var (vm, _, engine, status) = Create();
        engine.Failing.Add("session/list");

        await vm.RefreshAsync();

        Assert.Empty(vm.Sessions);
        Assert.Contains(status.LogEntries, line => line.Contains("could not list sessions"));
    }

    [Fact]
    public async Task AnEmptyStoreIsAnEmptyListNotAnError()
    {
        var (vm, _, engine, _) = Create();
        engine.Responses["session/list"] = new { sessions = Array.Empty<object>() };

        await vm.RefreshAsync();

        Assert.Empty(vm.Sessions);
        Assert.False(vm.EmptyBecauseOff, "no preference known: a plain empty list");
    }

    [Fact]
    public async Task AnEmptyListExplainsItselfWhenRetentionIsOff()
    {
        var preferences = new AppPreferences(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        var engine = new FakeEngineClient(autoNotify: false);
        var status = new StatusBarViewModel();
        var consultation = new ConsultationViewModel(
            new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(),
            status, new FakeDialogService(), TestSession.Page(engine, status), TestSession.Guidance(status));
        var vm = new SessionsViewModel(new EngineApi(engine), status, consultation, new FakeDialogService(), new RecordingNavigationService(), preferences);
        engine.Responses["session/list"] = new { sessions = Array.Empty<object>() };

        await vm.RefreshAsync();
        Assert.True(vm.EmptyBecauseOff, "keep is off by default and nothing is stored");

        ScriptOneSession(engine);  // history recorded while keep was on still shows
        await vm.RefreshAsync();
        Assert.False(vm.EmptyBecauseOff);
        Assert.Single(vm.Sessions);
    }
}
