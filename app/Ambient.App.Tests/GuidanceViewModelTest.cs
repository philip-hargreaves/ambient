using System.Text.Json;
using Ambient.App.Core.ViewModels;

namespace Ambient.App.Tests;

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
        GuidanceRecommendation.From(JsonSerializer.SerializeToElement(result), "NICE", true);

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

    private static JsonElement Record(object[] shown, bool stale = false) =>
        JsonSerializer.SerializeToElement(new
        {
            version = 1,
            noteRevision = 1,
            generatedAt = "2026-09-13T01:00:00Z",
            stale,
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
        Assert.Equal("NICE", card.SourceLabel);
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
        Assert.Equal(["NICE", "Fixture guidance corpus"], cards.Select(c => c.SourceLabel));
        Assert.Equal("Matched: the note as a whole", cards[^1].Recommendations.Single().Matched);
        Assert.False(cards[1].Recommendations.Single().CanOpen);

        guidance.ApplyCorpora(Fixtures.Load("guidance-corpora.json").GetProperty("result"));
        Assert.Equal(GuidanceReadiness.Ready, guidance.Readiness);
        Assert.Equal(
            "nice-2026-08-25: corpus.db sha256 does not match the manifest",
            guidance.RefusedCorpora.Single());
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
        Assert.Equal("Guidance could not be searched - see the status bar",
            session.Guidance.StateCaption);
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
        Assert.Equal("Search failed - see the status bar", session.Guidance.QueryCaption);
        Assert.True(session.Guidance.QueryShown);
    }

    [Fact]
    public async Task AFailedCorporaPollReadsAsUnavailable()
    {
        var engine = new FakeEngineClient(autoNotify: false)
        {
            FailNext = m => m == "guidance/corpora" ? new IOException("pipe closed") : null,
        };
        var status = new StatusBarViewModel();
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), status);

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
        Assert.Equal(["fx200-1_1_1", "fx200-1_1_2"], Shown(guidance));
        Assert.All(guidance.Cards.Single().Recommendations, r => Assert.False(r.MatchedVisible));
        Assert.Equal(GuidanceSection.Results, guidance.Section);

        // The note's own result lands behind the query and shows only after Clear
        engine.RaiseNotification("guidance/ready",
            Ready("abc", [Result("fx100-1_1_2"), Result("fx100-1_1_3")]));
        Assert.Equal(["fx200-1_1_1", "fx200-1_1_2"], Shown(guidance));

        guidance.ClearQueryCommand.Execute(null);
        Assert.False(guidance.QueryShown);
        Assert.Equal(["fx100-1_1_2", "fx100-1_1_3"], Shown(guidance));
    }

    [Fact]
    public async Task AQueryWithNoMatchSaysSoAndHidesTheNoteCaption()
    {
        var (session, engine, _) = await ReopenedAsync();
        session.Guidance.Query = "nothing here";
        await session.Guidance.SearchQueryCommand.ExecuteAsync(null);

        engine.RaiseNotification("guidance/ready", Ready(null, [], stale: null));

        Assert.Equal("No match in the installed guidance", session.Guidance.QueryCaption);
        Assert.False(session.Guidance.QuerySearching);
        Assert.Empty(session.Guidance.Cards);
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

        Assert.Equal("Search failed - see the status bar", session.Guidance.QueryCaption);
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
    public async Task AResultWithoutItsCorpusInSearchedHasNoLabels()
    {
        var (session, engine) = await AfterNoteAsync();

        engine.RaiseNotification("guidance/ready",
            Ready("s1", [Result("gout-1", source: "text")], searched: false));

        var card = session.Guidance.Cards.Single();
        Assert.Equal(GuidanceSection.Results, session.Guidance.Section);
        Assert.False(card.SourceLabelVisible);
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
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(),
            new StatusBarViewModel());
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
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(), status);

        Assert.Equal(GuidanceReadiness.Unavailable, session.Guidance.Readiness);
        Assert.Contains(
            status.LogEntries, e => e.Contains("embedding/default", StringComparison.Ordinal));
        Assert.DoesNotContain("embedding", status.LatestActivity, StringComparison.Ordinal);
        Assert.DoesNotContain("embedding", session.Guidance.StateCaption, StringComparison.Ordinal);
        session.Guidance.Query = "gout";
        Assert.False(session.Guidance.SearchEnabled);
    }

    [Fact]
    public void EveryCorpusRefusedReadsAsNoCorpus()
    {
        var engine = new FakeEngineClient(autoNotify: false);
        engine.GuidanceCorpora.Clear();
        engine.GuidanceCorpora.Add(new { id = "nice", unavailable = "sha256 differs" });
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), new NoteViewModel(),
            new StatusBarViewModel());

        Assert.Equal(GuidanceReadiness.NoCorpus, session.Guidance.Readiness);
        Assert.True(session.Guidance.NoCorpora);
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
        Assert.Equal("Matched: A sentence of the note.", found.Matched);

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
    }
}
