using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

namespace Ambient.App.Tests.Features.Consultation;

/// <summary>A written case standing in as the note of a demo record.</summary>
public class ExampleCaseTest
{
    private static readonly DemoCase Gout = new("Case 3, gout", "38-year-old man with recurrent effusions.");

    private static (ConsultationViewModel Session, FakeEngineClient Engine, NoteViewModel Note) Create()
    {
        var engine = new FakeEngineClient(autoNotify: false);
        var note = new NoteViewModel();
        var demo = new DemoMode(null, Path.Combine(Path.GetTempPath(), "missing.json"), [Gout]);
        var session = new ConsultationViewModel(new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), note, new StatusBarViewModel(), new FakeDialogService(), TestSession.Page(engine, new StatusBarViewModel()), demo: demo);
        return (session, engine, note);
    }

    [Fact]
    public void ParsesTitledBlocksAndSkipsUnderlines()
    {
        var cases = DemoCases.Parse("""
            Case 1, rheumatoid arthritis
            ----------------------------
            65-year-old man, three-month history of stiffness.
            Plan: refer urgently.

            Case 2, gout
            ------------
            38-year-old man.
            """);

        Assert.Equal(2, cases.Count);
        Assert.Equal("Case 1, rheumatoid arthritis", cases[0].Title);
        Assert.Equal("65-year-old man, three-month history of stiffness. Plan: refer urgently.", cases[0].Text);
        Assert.Equal("38-year-old man.", cases[1].Text);
        Assert.Empty(DemoCases.Parse(""));
    }

    [Fact]
    public void TheShippedCasesLoad()
    {
        var cases = DemoCases.Load();

        Assert.Equal(4, cases.Count);
        Assert.All(cases, c => Assert.StartsWith("Case ", c.Title));
        Assert.All(cases, c => Assert.True(c.Text.Split(' ').Length > 60, c.Title));
    }

    [Fact]
    public async Task ACaseStandsInAsTheNoteOfADemoRecordAndIsSearched()
    {
        var (session, engine, note) = Create();
        Assert.False(note.ExampleCasesVisible);
        Assert.True(await session.OpenStoredSessionAsync("s-copy", demo: true));
        Assert.True(note.ExampleCasesVisible);
        Assert.Equal(["Case 3, gout"], note.ExampleCaseTitles);

        note.ExampleCaseIndex = 0;

        Assert.Equal(Gout.Text, note.ClinicalNoteText);
        var saved = engine.Requests.Single(r => r.Method == "note/update");
        Assert.Contains("recurrent effusions", saved.Params);
        var search = engine.Requests.Single(r => r.Method == "guidance/search");
        Assert.Contains("s-copy", search.Params);

        await session.CloseReviewAsync();
        Assert.False(note.ExampleCasesVisible);
        Assert.Equal(-1, note.ExampleCaseIndex);
    }

    [Fact]
    public async Task ARealRecordNeverOffersACase()
    {
        var (session, engine, note) = Create();
        Assert.True(await session.OpenStoredSessionAsync("s-real"));

        Assert.False(note.ExampleCasesVisible);
        note.ExampleCaseIndex = 0;

        Assert.NotEqual(Gout.Text, note.ClinicalNoteText);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/update");
    }

    [Fact]
    public async Task ARegenerateClearsThePicker()
    {
        var (session, engine, note) = Create();
        Assert.True(await session.OpenStoredSessionAsync("s-copy", demo: true));
        note.ExampleCaseIndex = 0;

        await session.RegenerateNoteAsync();

        Assert.Equal(-1, note.ExampleCaseIndex);
        Assert.Contains(engine.Requests, r => r.Method == "note/regenerate");
    }
}
