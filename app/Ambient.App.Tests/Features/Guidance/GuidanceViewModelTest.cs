using System.Text.Json;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.Support;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

namespace Ambient.App.Tests.Features.Guidance;

/// <summary>
/// The Guidelines section's behaviour, driven through the consultation view model.
/// </summary>
public class GuidanceViewModelTest
{
    private static readonly object Corpus = new
    {
        id = "fixture",
        name = "Fixture guidance corpus",
        licence = "invented",
        attribution = "Fixture attribution",
        label = "NICE",
        source = "text",
        embedder = "gte-large-int8",
        sha256 = "",
        chunks = 40,
        builtAt = "2026-09-11T00:00:00Z",
        unavailable = (string?)null,
    };

    private static object Result(string chunkId, string trigger = "A sentence of the note.",
        string url = "https://example.test/fx100", string source = "nice",
        string number = "1.1.1", string section = "1.1 Referral", string updateTag = "",
        string lastUpdated = "", string corpus = "fixture") => new
        {
            corpus,
            chunkId,
            code = chunkId.Split('-')[0],
            number,
            title = "Fictional guideline",
            section,
            text = "Refer adults with persistent synovitis.",
            url,
            lastUpdated,
            updateTag,
            source,
            citation = "FX100 1.1.1, Fictional guideline",
            score = 0.897,
            trigger,
        };

    private static GuidanceRecommendation Found(object result) =>
        GuidanceRecommendation.From(JsonSerializer.SerializeToElement(result), "NICE", true, true);

    private static readonly object DocumentCorpus = new
    {
        id = "upload:6368831970660585267",
        name = "BSR PMR guidelines 2009",
        licence = "",
        attribution = "",
        source = "upload",
        embedder = "gte-large-int8",
        sha256 = "",
        chunks = 12,
        builtAt = "2026-09-15T09:13:02Z",
        unavailable = (string?)null,
    };

    private static object DocumentResult(int page = 1, int pages = 5, string number = "1.2",
        long document = 6368831970660585267L) => new
        {
            corpus = $"upload:{document}",
            chunkId = $"upload:{document}-4",
            code = "",
            number,
            title = "BSR PMR guidelines 2009",
            section = "",
            text = "Start prednisolone 15 mg daily.",
            url = "",
            lastUpdated = "2026-09-15T09:12:44Z",
            updateTag = "",
            source = "upload",
            citation = "BSR PMR guidelines 2009, page 2, 1.2 (added 15 Sep 2026)",
            score = 0.9,
            trigger = "",
            document,
            page,
            pages,
        };

    private static IEnumerable<string> Shown(GuidanceViewModel guidance) =>
        guidance.Cards.SelectMany(c => c.Recommendations).Select(r => r.ChunkId);

    private static JsonElement Ready(string? id, object[] shown, bool searched = true,
        bool? stale = false, string? storeError = null, object[]? corpora = null) =>
        JsonSerializer.SerializeToElement(new
        {
            id,
            storeError,
            stale,
            version = 1,
            noteRevision = 1,
            shown,
            searched = corpora ?? (searched ? new[] { Corpus } : []),
            considered = shown.Length,
            floor = 0.85,
            abstained = shown.Length == 0,
        });

    private static JsonElement Failed(string? id, string detail) =>
        JsonSerializer.SerializeToElement(new { id, detail });

    private static JsonElement Record(
        object[] shown, bool stale = false, bool documentsChanged = false) =>
        JsonSerializer.SerializeToElement(new
        {
            version = 1,
            noteRevision = 1,
            generatedAt = "2026-09-13T01:00:00Z",
            stale,
            documentsChanged,
            shown,
            searched = new[] { Corpus },
            considered = shown.Length,
            floor = 0.85,
            abstained = shown.Length == 0,
        });

    // Stop a consultation and let the note arrive: the section is searching for "s1"
    private static async Task<(ConsultationViewModel Session, FakeEngineClient Engine)>
        AfterNoteAsync()
    {
        var (session, engine, _) = TestSession.Create();
        await session.StartRecordingAsync();
        await session.StopRecordingAsync();
        engine.RaiseNotification("note/ready",
            JsonSerializer.SerializeToElement(new { text = "the clinical note" }));
        return (session, engine);
    }

    // Reopen stored consultation "abc": its note was searched (a record) or never was (null)
    private static async Task<(
        ConsultationViewModel Session, FakeEngineClient Engine, NoteViewModel Note)>
        ReopenedAsync(JsonElement? record = null)
    {
        var (session, engine, note) = TestSession.Create();
        engine.StoredNote = "the stored note";
        engine.StoredGuidance = record;
        await session.OpenStoredSessionAsync("abc");
        return (session, engine, note);
    }

    [Fact]
    public async Task ReopeningWithARecordShowsItFresh()
    {
        var (session, engine, _) =
            await ReopenedAsync(Record([Result("fx100-1_1_1"), Result("fx100-1_1_2")]));

        var guidance = session.Guidance;
        Assert.Equal(GuidanceSection.Results, guidance.Section);
        Assert.Equal(2, guidance.Cards.Single().Recommendations.Count);
        Assert.Equal("", guidance.FoundIn);
        Assert.False(guidance.Stale);
        Assert.True(guidance.HasRecord);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "guidance/search");
    }

    [Fact]
    public async Task ReopeningWithoutARecordIsNotSearched()
    {
        var (session, engine, _) = await ReopenedAsync();

        Assert.Equal(GuidanceSection.NotSearched, session.Guidance.Section);
        Assert.Equal(
            "Guidance was not searched for this consultation", session.Guidance.StateCaption);
        Assert.True(session.Guidance.SearchNoteCommand.CanExecute(null));
        Assert.DoesNotContain(engine.Requests, r => r.Method == "guidance/search");
    }

    [Fact]
    public async Task AnEmptyNoteAsksForNoGuidance()
    {
        var (session, engine, _) = TestSession.Create();
        engine.StoredGuidance = Record([Result("fx100-1_1_1")]);

        await session.OpenStoredSessionAsync("abc");

        Assert.Equal(GuidanceSection.Hidden, session.Guidance.Section);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "session/guidance");
    }

    [Fact]
    public async Task AStaleRecordLoadsWithTheWarning()
    {
        var (session, _, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")], stale: true));

        Assert.True(session.Guidance.Stale);
        Assert.True(session.Guidance.SearchAgainVisible);
    }

    [Fact]
    public async Task TheSharedFixtureRecordLoads()
    {
        var (session, _, _) = await ReopenedAsync(Fixtures.Load("session-guidance.json")
            .GetProperty("result").GetProperty("guidance"));

        var card = session.Guidance.Cards.Single();
        Assert.Equal(
            "Fictional inflammatory joint disease: assessment and management", card.Title);
        Assert.Equal("Updated 12 Oct 2020 · 1 recommendation", card.Meta);
        Assert.Equal("NICE · FX100", card.Chip);
        Assert.Equal("nice", card.Source);
        var found = card.Recommendations.Single();
        Assert.Equal("FX100 1.1.1", found.Reference);
        Assert.Equal("[2009, amended 2018]", found.Tag);
        Assert.Equal(
            "1.1 Referral, diagnosis and investigations › Referral from primary care", found.Path);
        Assert.True(found.CanOpen);
    }

    [Fact]
    public async Task TheSharedReadyAndCorporaFixturesLoad()
    {
        var (session, _) = await AfterNoteAsync();
        var guidance = session.Guidance;

        guidance.ApplyReady(Fixtures.Load("guidance-ready.json").GetProperty("params"));
        Assert.Equal(GuidanceSection.Results, guidance.Section);
        var cards = guidance.Cards;
        Assert.Equal(["NICE · FX100", "Fixture guidance corpus"], cards.Select(c => c.Chip));
        Assert.Equal("Matched: the note as a whole", cards[^1].Recommendations.Single().Matched);
        Assert.False(cards[1].Recommendations.Single().CanOpen);

        guidance.ApplyCorpora(Protocol.Parse<CorporaStatus>(Fixtures.Load("guidance-corpora.json").GetProperty("result"))!);
        Assert.Equal(GuidanceReadiness.Ready, guidance.Readiness);
        Assert.Equal(
            "nice-2026-08-25: corpus.db sha256 does not match the manifest",
            guidance.RefusedCorpora.Single());
    }

    [Fact]
    public async Task AnAddedDocumentLeadsWithItsChipAndADividerBeforeTheGuidelines()
    {
        var (session, _) = await AfterNoteAsync();
        session.Guidance.ApplyReady(Ready("s1", [Result("fx100-1_1_1"), DocumentResult()],
            corpora: [Corpus, DocumentCorpus]));

        var cards = session.Guidance.Cards;
        Assert.Equal(2, cards.Count);
        Assert.True(cards[0].FromDocument);
        Assert.Equal("Added document", cards[0].Chip);
        Assert.Equal("BSR PMR guidelines 2009", cards[0].Title);
        Assert.Equal("5 pages · added 15 Sep 2026 · 1 recommendation", cards[0].Meta);
        Assert.False(cards[0].DividerVisible);
        Assert.Equal("From NICE", cards[1].Divider);
        Assert.Equal("1 added document · 1 guideline", session.Guidance.Summary);

        var found = cards[0].Recommendations.Single();
        Assert.Equal(6368831970660585267L, found.Document);
        Assert.Equal("Page 2", found.PageLabel);
        Assert.Equal("1.2", found.Reference);
        Assert.True(found.ShowVisible);
        Assert.True(found.CanOpen);
        Assert.Equal("Open BSR PMR guidelines 2009", found.OpenName);
        Assert.Equal(
            "BSR PMR guidelines 2009, page 2, 1.2 (added 15 Sep 2026)", found.CitationText);
        Assert.False(cards[1].Recommendations.Single().ShowVisible);
    }

    [Fact]
    public async Task ShowInDocumentOpensThePageViewOnThePassage()
    {
        var (session, engine) = await AfterNoteAsync();
        engine.PageReply = new
        {
            path = @"C:\scratch\page.bmp",
            width = 1000,
            height = 1400,
            pages = 5,
            boxes = new[] { new { page = 1, left = 0.1, top = 0.2, right = 0.6, bottom = 0.3 } },
        };
        session.Guidance.ApplyReady(Ready("s1", [DocumentResult()], corpora: [DocumentCorpus]));
        var found = session.Guidance.Cards.Single().Recommendations.Single();

        await session.Guidance.ShowInDocumentAsync(found);

        Assert.True(session.PageView.Visible);
        Assert.Equal("Page 2 of 5", session.PageView.PageLabel);
        Assert.Contains("\"id\":6368831970660585267", engine.Requests[^1].Params);
        Assert.Single(session.PageView.Boxes);
        Assert.Equal("1 added document · 1 recommendation", session.Guidance.Summary);

        session.Guidance.Reset();
        session.PageView.Hide();
        Assert.False(session.PageView.Visible);
    }

    [Fact]
    public async Task TheNotesSearchStartsWithTheNoteAndEndsOnItsResult()
    {
        var (session, engine, _) = TestSession.Create();
        await session.StartRecordingAsync();
        await session.StopRecordingAsync();
        Assert.Equal(GuidanceSection.FollowsNote, session.Guidance.Section);

        engine.RaiseNotification("note/ready",
            JsonSerializer.SerializeToElement(new { text = "the clinical note" }));
        Assert.Equal(GuidanceSection.Searching, session.Guidance.Section);
        Assert.False(session.Guidance.SearchEnabled);

        engine.RaiseNotification("guidance/ready", Ready("s1", [Result("fx100-1_1_1")]));
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.False(session.Guidance.Stale);
    }

    [Fact]
    public async Task AResultThatArrivesBeforeTheNoteIsKept()
    {
        var (session, engine, _) = TestSession.Create();
        await session.StartRecordingAsync();
        await session.StopRecordingAsync();

        engine.RaiseNotification("guidance/ready", Ready("s1", [Result("fx100-1_1_1")]));
        engine.RaiseNotification("note/ready",
            JsonSerializer.SerializeToElement(new { text = "the clinical note" }));

        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
    }

    [Fact]
    public async Task AFailureThatArrivesBeforeTheNoteStands()
    {
        var (session, engine, _) = TestSession.Create();
        await session.StartRecordingAsync();
        await session.StopRecordingAsync();

        engine.RaiseNotification("guidance/failed", Failed("s1", "embedder gone"));
        engine.RaiseNotification("note/ready",
            JsonSerializer.SerializeToElement(new { text = "the clinical note" }));

        Assert.Equal(GuidanceSection.Failed, session.Guidance.Section);
        Assert.True(session.Guidance.SearchNoteCommand.CanExecute(null));
    }

    [Fact]
    public async Task AResultForAnotherSessionIsIgnored()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready", Ready("other", [Result("fx100-1_1_1")]));

        Assert.Equal(GuidanceSection.Searching, session.Guidance.Section);
        Assert.Empty(session.Guidance.Cards);
        Assert.Contains(
            session.Status.LogEntries, e => e.Contains("other", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASupersededFailureArrivingBeforeTheReplyChangesNothing()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        engine.BeforeReply = method =>
        {
            if (method == "guidance/search")
            {
                engine.RaiseNotification("guidance/failed", Failed("abc", "superseded"));
            }
        };

        await session.Guidance.SearchNoteCommand.ExecuteAsync(null);

        Assert.Equal(GuidanceSection.Searching, session.Guidance.Section);
        engine.RaiseNotification("guidance/ready", Ready("abc", [Result("fx100-1_1_2")]));
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
    }

    [Fact]
    public async Task AnyOtherFailureShowsTheFailedStateAndKeepsTheDetailOffScreen()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/failed",
            Failed("s1", "guidance embedder gte-large-int8: tokenizer ignores max_length"));

        Assert.Equal(GuidanceSection.Failed, session.Guidance.Section);
        Assert.Equal("Guidance could not be searched.", session.Guidance.StateCaption);
        Assert.Contains(
            session.Status.LogEntries, e => e.Contains("tokenizer", StringComparison.Ordinal));
        Assert.DoesNotContain("tokenizer", session.Status.LatestActivity, StringComparison.Ordinal);
        Assert.True(session.Guidance.SearchNoteCommand.CanExecute(null));
    }

    [Fact]
    public async Task LosingTheEngineEndsTheSearchAndReconnectingPollsAgain()
    {
        var (session, engine) = await AfterNoteAsync();
        var polls = engine.Requests.Count(r => r.Method == "guidance/corpora");

        engine.SetConnected(false);
        Assert.Equal(GuidanceSection.NotSearched, session.Guidance.Section);

        engine.SetConnected(true);
        Assert.Equal(polls + 1, engine.Requests.Count(r => r.Method == "guidance/corpora"));
    }

    [Fact]
    public async Task LosingTheEngineDuringATypedQueryFailsIt()
    {
        var (session, engine, _) = await ReopenedAsync();
        session.Guidance.Query = "gout";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);

        engine.SetConnected(false);

        Assert.False(session.Guidance.QuerySearching);
        Assert.Equal("Search failed.", session.Guidance.QueryCaption);
        Assert.True(session.Guidance.QueryShown);
    }

    [Fact]
    public void AFailedCorporaPollReadsAsUnavailable()
    {
        var engine = new FakeEngineClient(autoNotify: false)
        {
            FailNext = m => m == "guidance/corpora" ? new IOException("pipe closed") : null,
        };
        var status = new StatusBarViewModel();
        var session = new ConsultationViewModel(new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), status, new FakeDialogService(), TestSession.Page(engine, status));

        Assert.Equal(GuidanceReadiness.Unavailable, session.Guidance.Readiness);
        Assert.Contains(
            status.LogEntries, e => e.Contains("pipe closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SavingAChangedNoteMarksTheGuidanceStale()
    {
        var (session, engine, note) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));

        note.ClinicalNoteText = "edited";
        await session.SaveNoteAsync();

        Assert.True(session.Guidance.Stale);
        Assert.Contains(engine.Requests, r => r.Method == "note/update");
    }

    [Fact]
    public async Task ChangedDocumentsMarkAStoredResultStaleWithTheirOwnCaption()
    {
        var (session, engine, note) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));

        engine.RaiseNotification("guidance/documentsChanged",
            JsonSerializer.SerializeToElement(new { }));

        Assert.True(session.Guidance.Stale);
        Assert.Equal(
            "Added documents changed since this guidance was found.",
            session.Guidance.StaleCaption);

        note.ClinicalNoteText = "edited";
        await session.SaveNoteAsync();

        Assert.Equal(
            "This guidance was found before your note edits.", session.Guidance.StaleCaption);
    }

    [Fact]
    public async Task ABurstOfDocumentChangesSearchesTheNoteOnceAfterTheySettle()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        session.DocumentsSettle = TimeSpan.FromMilliseconds(80);
        var before = engine.Requests.Count(r => r.Method == "guidance/search");

        // Thirty documents finishing in quick succession
        for (var i = 0; i < 30; i++)
        {
            engine.RaiseNotification("guidance/documentsChanged",
                JsonSerializer.SerializeToElement(new { }));
        }

        Assert.True(session.Guidance.Stale);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (engine.Requests.Count(r => r.Method == "guidance/search") == before
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await Task.Delay(300);  // long enough for any second search to have fired
        Assert.Equal(before + 1, engine.Requests.Count(r => r.Method == "guidance/search"));
    }

    [Fact]
    public void EverySearchAnnouncesItsTimingEvenWhenTheTextRepeats()
    {
        var guidance = new GuidanceViewModel();
        var announced = 0;
        guidance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GuidanceViewModel.FoundIn) && guidance.FoundIn.Length > 0)
            {
                announced++;
            }
        };

        guidance.SearchStarted();
        guidance.ApplyReady(Ready("abc", [Result("fx100-1_1_2")]));
        guidance.SearchStarted();
        guidance.ApplyReady(Ready("abc", [Result("fx100-1_1_2")]));

        Assert.Equal(2, announced);
        Assert.Equal("found in 0.0 s", guidance.FoundIn);
    }

    [Fact]
    public async Task AReopenedNoteEditedAndOlderThanTheDocumentsKeepsTheNoteCaption()
    {
        var (session, _, _) = await ReopenedAsync(
            Record([Result("fx100-1_1_2")], stale: true, documentsChanged: true));

        Assert.True(session.Guidance.Stale);
        Assert.Equal(
            "This guidance was found before your note edits.", session.Guidance.StaleCaption);
    }

    [Fact]
    public async Task AReopenedNoteOlderThanTheDocumentsIsStaleAndSearchesAgainByItself()
    {
        var (session, engine, _) = await ReopenedAsync(
            Record([], documentsChanged: true));
        session.DocumentsSettle = TimeSpan.FromMilliseconds(80);

        Assert.True(session.Guidance.Stale);
        Assert.Equal(
            "Added documents changed since this guidance was found.",
            session.Guidance.StaleCaption);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!engine.Requests.Any(r => r.Method == "guidance/search")
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Contains(engine.Requests, r => r.Method == "guidance/search");
    }

    [Fact]
    public async Task DocumentChangesLeaveATypedQueryAlone()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        session.DocumentsSettle = TimeSpan.FromMilliseconds(50);
        session.Guidance.Query = "allopurinol";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);
        var before = engine.Requests.Count(r => r.Method == "guidance/search");

        engine.RaiseNotification("guidance/documentsChanged",
            JsonSerializer.SerializeToElement(new { }));
        await Task.Delay(300);

        Assert.Equal(before, engine.Requests.Count(r => r.Method == "guidance/search"));
    }

    [Fact]
    public async Task SavingAnUnchangedNoteSendsNothingAndMarksNothing()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));

        await session.SaveNoteAsync();

        Assert.False(session.Guidance.Stale);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/update");
    }

    [Fact]
    public async Task RegenerateSearchesAgainAndTheResultClearsStale()
    {
        var (session, engine, note) =
            await ReopenedAsync(Record([Result("fx100-1_1_1")], stale: true));

        await note.RegenerateCommand.ExecuteAsync(null);
        Assert.Equal(GuidanceSection.FollowsNote, session.Guidance.Section);
        Assert.False(session.Guidance.Stale);
        Assert.Empty(session.Guidance.Cards);

        engine.RaiseNotification("note/ready",
            JsonSerializer.SerializeToElement(new { text = "rewritten" }));
        engine.RaiseNotification("guidance/ready", Ready("abc", [Result("fx100-1_1_2")]));
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.False(session.Guidance.Stale);
    }

    [Fact]
    public async Task SearchAgainSavesAChangedNoteThenSearchesTheSession()
    {
        var (session, engine, note) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        note.ClinicalNoteText = "edited";

        await session.Guidance.SearchNoteCommand.ExecuteAsync(null);

        var methods = engine.Requests.Select(r => r.Method).ToList();
        var save = methods.IndexOf("note/update");
        Assert.True(save >= 0 && save < methods.LastIndexOf("guidance/search"));
        Assert.Contains(engine.Requests,
            r => r.Method == "guidance/search"
                && r.Params.Contains("\"id\":\"abc\"", StringComparison.Ordinal));
        Assert.Equal(GuidanceSection.Searching, session.Guidance.Section);
        Assert.False(session.Guidance.Stale, "a search in flight is not stale");
    }

    [Fact]
    public async Task OpeningAnotherConsultationDuringTheSaveLeavesItAlone()
    {
        var (session, engine, note) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        note.ClinicalNoteText = "edited";
        var opened = false;  // the open autosaves too, so the hook fires once
        engine.BeforeReply = method =>
        {
            if (method == "note/update" && !opened)
            {
                opened = true;
                _ = session.OpenStoredSessionAsync("other");
            }
        };

        await session.Guidance.SearchNoteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(engine.Requests, r => r.Method == "guidance/search");
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.False(session.Guidance.Stale);
        Assert.DoesNotContain(
            "Note saved", session.Status.LatestActivity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATypedQueryStandsInForTheNotesCardsUntilCleared()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        var guidance = session.Guidance;

        guidance.Query = "gout flare";
        Assert.True(guidance.SearchEnabled);
        await guidance.SearchQueryCommand.ExecuteAsync(null);
        Assert.True(guidance.QuerySearching);
        Assert.False(guidance.SearchEnabled);
        Assert.False(guidance.QueryBoxEnabled);
        Assert.Contains(engine.Requests,
            r => r.Method == "guidance/search"
                && r.Params.Contains("\"text\":\"gout flare\"", StringComparison.Ordinal));

        Assert.True(guidance.QueryShown);
        Assert.Equal("Search: 'gout flare'", guidance.QueryHeader);
        Assert.Empty(guidance.Cards);

        engine.RaiseNotification("guidance/ready",
            Ready(null, [Result("fx200-1_1_1", trigger: ""), Result("fx200-1_1_2", trigger: "")],
                stale: null));
        Assert.Equal("2 results", guidance.QueryCaption);
        Assert.Matches(@"^found in \d+\.\d s$", guidance.FoundIn);
        Assert.Equal(["fx200-1_1_1", "fx200-1_1_2"], Shown(guidance));
        Assert.All(guidance.Cards.Single().Recommendations, r => Assert.False(r.MatchedVisible));
        Assert.Equal(GuidanceSection.Results, guidance.Section);

        // The note's own result lands behind the query and shows only after Clear
        engine.RaiseNotification("guidance/ready",
            Ready("abc", [Result("fx100-1_1_2"), Result("fx100-1_1_3")]));
        Assert.Equal(["fx200-1_1_1", "fx200-1_1_2"], Shown(guidance));

        guidance.ClearQueryCommand.Execute(null);
        Assert.False(guidance.QueryShown);
        Assert.Equal("", guidance.Query);
        Assert.Equal("", guidance.FoundIn);
        Assert.Equal(["fx100-1_1_2", "fx100-1_1_3"], Shown(guidance));
    }

    [Fact]
    public async Task AQueryWithNoMatchSaysSoAndHidesTheNoteCaption()
    {
        var (session, engine, _) = await ReopenedAsync();
        session.Guidance.Query = "nothing here";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);

        engine.RaiseNotification("guidance/ready", Ready(null, [], stale: null));

        Assert.Equal("Nothing came close enough to show.", session.Guidance.QueryCaption);
        Assert.False(session.Guidance.QuerySearching);
        Assert.Empty(session.Guidance.Cards);
        Assert.Equal("", session.Guidance.Summary);
        Assert.False(session.Guidance.CardsVisible);
        Assert.Equal(GuidanceSection.NotSearched, session.Guidance.Section);
        Assert.False(session.Guidance.CaptionVisible, "the query bar replaces the note caption");

        session.Guidance.ClearQueryCommand.Execute(null);
        Assert.True(session.Guidance.CaptionVisible);
    }

    [Fact]
    public async Task ATypedQueryFailureShowsInTheQueryBarWithTheDetailLogged()
    {
        var (session, engine, _) = await ReopenedAsync();
        session.Guidance.Query = "gout";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);

        engine.RaiseNotification("guidance/failed", Failed(null, "embedder gone"));

        Assert.Equal("Search failed.", session.Guidance.QueryCaption);
        Assert.Contains(
            session.Status.LogEntries, e => e.Contains("embedder gone", StringComparison.Ordinal));
        Assert.Equal(GuidanceSection.NotSearched, session.Guidance.Section);
    }

    [Fact]
    public async Task AQueryReplyAfterClearOrANewConsultationIsIgnored()
    {
        var (session, engine, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")]));
        session.Guidance.Query = "gout";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);
        session.Guidance.ClearQueryCommand.Execute(null);

        engine.RaiseNotification("guidance/ready", Ready(null, [], stale: null));
        Assert.Equal("", session.Guidance.QueryCaption);
        Assert.False(session.Guidance.QueryShown);

        session.Guidance.Query = "gout";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);
        await session.CloseReviewAsync();
        engine.RaiseNotification("guidance/failed", Failed(null, "late"));
        Assert.Equal("", session.Guidance.QueryCaption);
        Assert.Empty(session.Guidance.Cards);
    }

    [Fact]
    public async Task TheSearchBoxWaitsWhileTheNoteIsSearched()
    {
        var (session, _) = await AfterNoteAsync();
        session.Guidance.Query = "gout";

        Assert.False(session.Guidance.QueryBoxEnabled);
        Assert.False(session.Guidance.SearchEnabled);
    }

    [Fact]
    public void TheSearchBoxWaitsWhileTheSectionIsHidden()
    {
        var (session, _, _) = TestSession.Create();
        session.Guidance.Query = "gout";

        Assert.False(session.Guidance.Visible);
        Assert.False(session.Guidance.QueryBoxEnabled);
        Assert.False(session.Guidance.SearchQueryCommand.CanExecute(null));
    }

    [Fact]
    public async Task NothingMatchedAndNoCorpusReadDifferently()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready", Ready("s1", []));
        Assert.Equal(GuidanceSection.NothingMatched, session.Guidance.Section);

        engine.RaiseNotification("guidance/ready", Ready("s1", [], searched: false));
        Assert.Equal(GuidanceSection.NoCorpusAtSearch, session.Guidance.Section);
    }

    [Fact]
    public async Task AResultWithoutItsCorpusInSearchedShowsItsCode()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready",
            Ready("s1", [Result("gout-1", source: "text")], searched: false));

        var card = session.Guidance.Cards.Single();
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.Equal("GOUT", card.Chip);
    }

    [Fact]
    public async Task AResultTheEngineCouldNotStoreStillShows()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready",
            Ready("s1", [Result("fx100-1_1_1")], storeError: "disk full"));

        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.True(session.Guidance.NotStored);
        Assert.Contains(
            session.Status.LogEntries, e => e.Contains("disk full", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadinessComesFromThePollThenTheNotification()
    {
        var engine = new FakeEngineClient(autoNotify: false) { GuidanceState = "loading" };
        var session = new ConsultationViewModel(new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), new StatusBarViewModel(), new FakeDialogService(), TestSession.Page(engine, new StatusBarViewModel()));
        Assert.Equal(GuidanceReadiness.Loading, session.Guidance.Readiness);

        engine.GuidanceState = "ready";
        engine.RaiseNotification("guidance/model",
            JsonSerializer.SerializeToElement(new { state = "ready", detail = (string?)null }));
        Assert.Equal(GuidanceReadiness.Ready, session.Guidance.Readiness);
        Assert.Equal(2, engine.Requests.Count(r => r.Method == "guidance/corpora"));
    }

    [Fact]
    public void AnUnavailableEmbedderLogsItsReasonWithoutShowingIt()
    {
        var engine = new FakeEngineClient(autoNotify: false)
        {
            GuidanceState = "unavailable",
            GuidanceDetail = "no model for embedding/default",
        };
        var status = new StatusBarViewModel();
        var session = new ConsultationViewModel(new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), status, new FakeDialogService(), TestSession.Page(engine, status));

        Assert.Equal(GuidanceReadiness.Unavailable, session.Guidance.Readiness);
        Assert.Contains(
            status.LogEntries, e => e.Contains("embedding/default", StringComparison.Ordinal));
        Assert.DoesNotContain("embedding", status.LatestActivity, StringComparison.Ordinal);
        Assert.DoesNotContain("embedding", session.Guidance.StateCaption, StringComparison.Ordinal);
        session.Guidance.Query = "gout";
        Assert.False(session.Guidance.SearchEnabled);
    }

    [Fact]
    public void NoInstalledCorpusStillSearchesSinceAddedDocumentsCan()
    {
        var engine = new FakeEngineClient(autoNotify: false);
        engine.GuidanceCorpora.Clear();
        engine.GuidanceCorpora.Add(new { id = "nice", unavailable = "sha256 differs" });
        var session = new ConsultationViewModel(new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), new StatusBarViewModel(), new FakeDialogService(), TestSession.Page(engine, new StatusBarViewModel()));

        Assert.Equal(GuidanceReadiness.Ready, session.Guidance.Readiness);
        Assert.False(session.Guidance.SettingsLinkVisible);
        Assert.Equal(["nice: sha256 differs"], session.Guidance.RefusedCorpora);
    }

    [Fact]
    public async Task ANewConsultationClearsTheSection()
    {
        var (session, _, _) = await ReopenedAsync(Record([Result("fx100-1_1_1")], stale: true));

        await session.CloseReviewAsync();

        Assert.Equal(GuidanceSection.Hidden, session.Guidance.Section);
        Assert.Empty(session.Guidance.Cards);
        Assert.False(session.Guidance.Stale);
    }

    [Fact]
    public void OpenIsOfferedForWebLinksOnly()
    {
        var web = Found(Result("fx100-1_1_1"));
        var file = Found(Result("gout-1", url: "Gout.md", source: "text"));
        var none = Found(Result("x-1", url: ""));

        Assert.True(web.CanOpen);
        Assert.False(file.CanOpen);
        Assert.False(none.CanOpen);
        Assert.Equal(
            "FX100 1.1.1, Fictional guideline\nhttps://example.test/fx100", web.CitationText);
        Assert.Equal("FX100 1.1.1, Fictional guideline", file.CitationText);
    }

    [Fact]
    public void RecommendationsDisplayAsTheGuidelineWrites()
    {
        var bare = Found(Result("fx100-1_1_1", number: "", section: "", updateTag: "2015"));
        Assert.Equal("FX100", bare.Reference);
        Assert.Equal("[2015]", bare.Tag);
        Assert.False(bare.PathVisible);
        Assert.Equal("Open FX100", bare.OpenName);

        var found = Found(Result("fx100-1_1_1"));
        Assert.Equal("1.1 Referral", found.Path);
        Assert.Equal("Matched: “A sentence of the note.”", found.Matched);
        Assert.True(found.LabelVisible);

        var whole = Found(Result("fx100-1_1_1", trigger: ""));
        Assert.Equal("Matched: the note as a whole", whole.Matched);

        var typed = GuidanceRecommendation.From(
            JsonSerializer.SerializeToElement(Result("fx100-1_1_1", trigger: "")), "NICE", false);
        Assert.False(typed.MatchedVisible);
    }

    [Fact]
    public void ACardIsOneGuidelineWithItsRecommendationsInOrder()
    {
        var cards = GuidanceCard.Group(
        [
            Found(Result("fx100-1_1_1", lastUpdated: "2020-10-12T09:30:00Z")),
            Found(Result("fx200-1_1_1")),
            Found(Result("fx100-1_1_2", lastUpdated: "2020-10-12T09:30:00Z")),
        ]).ToList();

        Assert.Equal(["FX100", "FX200"], cards.Select(c => c.Code));
        Assert.Equal("Fictional guideline", cards[0].Title);
        Assert.Equal(["fx100-1_1_1", "fx100-1_1_2"],
            cards[0].Recommendations.Select(r => r.ChunkId));
        Assert.Equal("Updated 12 Oct 2020 · 2 recommendations", cards[0].Meta);
        Assert.Equal("1 recommendation", cards[1].Meta);
        Assert.Equal("NICE · FX100", cards[0].Chip);
    }

    [Fact]
    public async Task TheWholeNoteMatchesComeLast()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready", Ready("s1",
        [
            Result("fx100-1_1_1", trigger: ""),
            Result("fx100-1_1_2", trigger: "Second sentence."),
            Result("fx100-1_1_3", trigger: ""),
        ]));

        Assert.Single(session.Guidance.Cards);
        Assert.Equal(["fx100-1_1_2", "fx100-1_1_1", "fx100-1_1_3"], Shown(session.Guidance));
        Assert.Equal("1 guideline · 3 recommendations", session.Guidance.Summary);
        Assert.True(session.Guidance.CardsVisible);
        Assert.Matches(@"^found in \d+\.\d s$", session.Guidance.FoundIn);
    }

    [Fact]
    public async Task AGuidelineFoundBySentenceLeadsOneFoundByTheWholeNote()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready", Ready("s1",
        [
            Result("fx200-1_1_1", trigger: ""),
            Result("fx100-1_1_1", trigger: "Second sentence."),
            Result("fx200-1_1_2", trigger: "Third sentence."),
        ]));

        Assert.Equal(["FX100", "FX200"], session.Guidance.Cards.Select(c => c.Code));
        Assert.Equal(["fx100-1_1_1", "fx200-1_1_2", "fx200-1_1_1"], Shown(session.Guidance));
        Assert.Equal("2 guidelines · 3 recommendations", session.Guidance.Summary);
    }
}
