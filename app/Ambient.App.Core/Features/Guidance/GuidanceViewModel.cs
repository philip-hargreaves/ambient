using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.Client;

namespace Ambient.App.Core.Features.Guidance;

/// <summary>Whether the engine's embedder has loaded, so it can search at all.</summary>
public enum GuidanceReadiness
{
    Loading,
    Ready,
    Unavailable,
}

/// <summary>Where the note's own search has got to.</summary>
public enum GuidanceSection
{
    Hidden,
    FollowsNote,
    Searching,
    Results,
    NothingMatched,
    NoCorpusAtSearch,
    Failed,
    NotSearched,
}

/// <summary>
/// The Guidelines section under the note. The consultation view model owns the
/// engine and feeds this from the wire. Nothing here talks to it.
/// </summary>
public sealed partial class GuidanceViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateCaption), nameof(CaptionVisible),
        nameof(SettingsLinkVisible),
        nameof(QueryBoxEnabled), nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchNoteCommand), nameof(SearchQueryCommand))]
    public partial GuidanceReadiness Readiness { get; private set; } = GuidanceReadiness.Loading;

    /// <summary>The loader's reason when unavailable, for the log. Never shown.</summary>
    public string ReadinessDetail { get; private set; } = "";

    /// <summary>Corpora the engine refused, as "id: reason", for the log.</summary>
    public IReadOnlyList<string> RefusedCorpora { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible), nameof(Searching), nameof(Failed),
        nameof(NotSearched), nameof(StateCaption), nameof(CaptionVisible),
        nameof(SearchAgainVisible), nameof(QueryBoxEnabled), nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchNoteCommand), nameof(SearchQueryCommand))]
    public partial GuidanceSection Section { get; private set; } = GuidanceSection.Hidden;

    /// <summary>The note or the added documents have moved since this was found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchAgainVisible))]
    public partial bool Stale { get; private set; }

    [ObservableProperty]
    public partial string StaleCaption { get; private set; } = NoteStaleCaption;

    private const string NoteStaleCaption = "This guidance was found before your note edits.";

    private const string DocumentsStaleCaption =
        "Added documents changed since this guidance was found.";

    /// <summary>
    /// The engine showed these results but could not keep them with the session.
    /// </summary>
    [ObservableProperty]
    public partial bool NotStored { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchQueryCommand))]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueryCaption), nameof(QueryCaptionVisible),
        nameof(QueryBoxEnabled), nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchQueryCommand))]
    public partial bool QuerySearching { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueryCaption), nameof(QueryCaptionVisible))]
    public partial bool QueryFailed { get; private set; }

    /// <summary>"found in 0.4 s" for the search that put the cards on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoundInVisible))]
    public partial string FoundIn { get; private set; } = "";

    /// <summary>The note sentence under the pointer, for the note editor to light.</summary>
    [ObservableProperty]
    public partial string Hovered { get; set; } = "";

    /// <summary>A typed query's cards while one shows, otherwise the note's.</summary>
    public ObservableCollection<GuidanceCard> Cards { get; } = [];

    /// <summary>Set by the consultation view model, which owns the engine.</summary>
    public Func<Task>? SearchNoteRequested { get; set; }

    public Func<string, Task>? SearchQueryRequested { get; set; }

    /// <summary>The cards on screen were replaced.</summary>
    public Action? CardsShown { get; set; }

    /// <summary>A card's Show in document and Open, answered by the page view.</summary>
    public Func<GuidanceRecommendation, Task>? ShowInDocumentRequested { get; set; }

    public Func<GuidanceRecommendation, Task>? OpenDocumentRequested { get; set; }

    public Task ShowInDocumentAsync(GuidanceRecommendation found) =>
        ShowInDocumentRequested?.Invoke(found) ?? Task.CompletedTask;

    public Task OpenDocumentAsync(GuidanceRecommendation found) =>
        OpenDocumentRequested?.Invoke(found) ?? Task.CompletedTask;

    private readonly Stopwatch _noteClock = new();
    private readonly Stopwatch _queryClock = new();
    private List<GuidanceRecommendation> _noteResults = [];
    private List<GuidanceRecommendation>? _queryResults;
    private string _queryText = "";

    public bool Visible => Section != GuidanceSection.Hidden;

    public bool Searching => Section == GuidanceSection.Searching;

    public bool Failed => Section == GuidanceSection.Failed;

    public bool NotSearched => Section == GuidanceSection.NotSearched;

    public bool SettingsLinkVisible => Readiness == GuidanceReadiness.Unavailable;

    public bool HasRecord => Section is GuidanceSection.Results
        or GuidanceSection.NothingMatched or GuidanceSection.NoCorpusAtSearch;

    public bool CardsVisible => Cards.Count > 0;

    public bool FoundInVisible => FoundIn.Length > 0;

    /// <summary>"2 guidelines · 3 recommendations" for the cards on screen.</summary>
    public string Summary
    {
        get
        {
            if (Cards.Count == 0)
            {
                return "";
            }

            var found = Cards.Sum(c => c.Recommendations.Count);
            var documents = Cards.Count(c => c.FromDocument);
            var guidelines = Cards.Count - documents;
            if (documents > 0 && guidelines > 0)
            {
                return $"{Count(documents, "added document")} · {Count(guidelines, "guideline")}";
            }

            return documents > 0
                ? $"{Count(documents, "added document")} · {Count(found, "recommendation")}"
                : $"{Count(guidelines, "guideline")} · {Count(found, "recommendation")}";
        }
    }

    public bool SearchAgainVisible => Stale && !Searching;

    /// <summary>The box waits while either search runs. The button also needs a query.</summary>
    public bool QueryBoxEnabled => Readiness == GuidanceReadiness.Ready && Visible
        && !Searching && !QuerySearching;

    public bool SearchEnabled => QueryBoxEnabled && Query.Trim().Length > 0;

    public bool CaptionVisible => StateCaption.Length > 0 && !QueryShown;

    /// <summary>A typed query is on screen in place of the note's cards.</summary>
    public bool QueryShown => _queryText.Length > 0;

    public string QueryHeader => QueryShown ? $"Search: '{_queryText}'" : "";

    // What the section says when it has nothing to show: the search's own
    // state, or why the engine cannot search yet
    public string StateCaption => Section switch
    {
        GuidanceSection.Hidden or GuidanceSection.Results => "",
        GuidanceSection.NothingMatched => "Nothing came close enough to show. "
            + "Your documents or the installed guidance may still cover this condition.",
        GuidanceSection.NoCorpusAtSearch =>
            "No guidance was installed when this note was searched.",
        GuidanceSection.Failed => "Guidance could not be searched.",
        _ when Readiness != GuidanceReadiness.Ready => Readiness switch
        {
            GuidanceReadiness.Loading => "Loading the guidance model",
            _ => "Guidance is unavailable on this device.",
        },
        GuidanceSection.FollowsNote => "Guidance follows the note",
        GuidanceSection.Searching => "Finding guidance",
        _ => "Guidance was not searched for this consultation",
    };

    public string QueryCaption => QuerySearching ? "Searching"
        : QueryFailed ? "Search failed."
        : _queryResults is null ? ""
        : _queryResults.Count switch
        {
            0 => "Nothing came close enough to show.",
            1 => "1 result",
            var n => $"{n} results",
        };

    public bool QueryCaptionVisible => QueryCaption.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSearchNote))]
    private Task SearchNote() => SearchNoteRequested?.Invoke() ?? Task.CompletedTask;

    private bool CanSearchNote() => SearchNoteRequested is not null
        && Readiness == GuidanceReadiness.Ready && !Searching
        && Section is not (GuidanceSection.Hidden or GuidanceSection.FollowsNote);

    [RelayCommand(CanExecute = nameof(SearchEnabled))]
    private Task SearchQuery()
    {
        _queryText = Query.Trim();
        _queryResults = null;
        QueryFailed = false;
        QuerySearching = true;
        _queryClock.Restart();
        QueryChanged();
        return SearchQueryRequested?.Invoke(_queryText) ?? Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(QueryShown))]
    private void ClearQuery()
    {
        Query = "";
        _queryText = "";
        _queryResults = null;
        QuerySearching = false;
        QueryFailed = false;
        FoundIn = "";
        QueryChanged();
    }

    public void Reset()
    {
        _noteResults = [];
        Stale = false;
        NotStored = false;
        Query = "";
        Section = GuidanceSection.Hidden;
        ClearQuery();
    }

    /// <summary>The note is being written. Its search follows.</summary>
    public void NoteStarted()
    {
        _noteResults = [];
        Stale = false;
        NotStored = false;
        FoundIn = "";
        Section = GuidanceSection.FollowsNote;
        ShowCards();
    }

    /// <summary>The note arrived. A result or failure that beat it stands.</summary>
    public void NoteReady()
    {
        if (Section == GuidanceSection.FollowsNote)
        {
            Section = GuidanceSection.Searching;
            _noteClock.Restart();
        }
    }

    public void NoteFailed() => Section = GuidanceSection.Hidden;

    /// <summary>The consultation view model has accepted a search-again request.</summary>
    public void SearchStarted()
    {
        Stale = false;
        Section = GuidanceSection.Searching;
        _noteClock.Restart();
    }

    public void MarkStale()
    {
        if (HasRecord)
        {
            StaleCaption = NoteStaleCaption;
            Stale = true;
        }
    }

    /// <summary>guidance/documentsChanged: a note edit stays the stronger reason.</summary>
    public void DocumentsChanged()
    {
        if (HasRecord && !Stale)
        {
            StaleCaption = DocumentsStaleCaption;
            Stale = true;
        }
    }

    /// <summary>
    /// Whether the note's search should run again by itself once added documents settle:
    /// there is a result to refresh and no typed query on screen to clobber.
    /// </summary>
    public bool WantsSearchAfterDocuments =>
        HasRecord && !QueryShown && !Searching && Readiness == GuidanceReadiness.Ready;

    /// <summary>
    /// A reopened session's record, or null when it was never searched. True when the
    /// added documents have changed since, so the caller can search again.
    /// </summary>
    public bool LoadStored(JsonElement? guidance)
    {
        NotStored = false;
        FoundIn = "";
        if (guidance is not { ValueKind: JsonValueKind.Object } record)
        {
            _noteResults = [];
            Stale = false;
            Section = GuidanceSection.NotSearched;
            ShowCards();
            return false;
        }

        ApplyRecord(record);
        if (!Flag(record, "documentsChanged") || !HasRecord)
        {
            return false;
        }

        // A note edit stays the stronger reason, as it does while the session is open
        if (!Stale)
        {
            StaleCaption = DocumentsStaleCaption;
            Stale = true;
        }

        return true;
    }

    /// <summary>The note's search came back, for the consultation on screen.</summary>
    public void ApplyReady(JsonElement result)
    {
        ApplyRecord(result);
        NotStored = GuidanceCard.Field(result, "storeError").Length > 0;
        ShowFoundIn(_noteClock);
    }

    public void ApplyFailed() => Section = GuidanceSection.Failed;

    // Cleared first, so two searches of the same length still announce the second
    private void ShowFoundIn(Stopwatch clock)
    {
        FoundIn = "";
        FoundIn = Elapsed(clock);
    }

    // A query reply after Clear or a new consultation belongs to nothing on screen
    public void ApplyQueryReady(JsonElement result)
    {
        if (!QuerySearching)
        {
            return;
        }

        _queryResults = ReadResults(result, false);
        QuerySearching = false;
        QueryFailed = false;
        ShowFoundIn(_queryClock);
        QueryChanged();
    }

    public void ApplyQueryFailed()
    {
        if (!QuerySearching)
        {
            return;
        }

        QuerySearching = false;
        QueryFailed = true;
    }

    /// <summary>
    /// guidance/corpora: the embedder's state. Searching needs only that, since added
    /// documents are searched whether or not any corpus is installed.
    /// </summary>
    public void ApplyCorpora(CorporaStatus corpora)
    {
        ReadinessDetail = corpora.Detail ?? "";
        var refused = new List<string>();
        foreach (var corpus in corpora.Corpora)
        {
            var reason = corpus.Unavailable ?? "";
            if (reason.Length > 0)
            {
                refused.Add($"{corpus.Id}: {reason}");
            }
        }

        RefusedCorpora = refused;
        Readiness = corpora.State switch
        {
            "loading" => GuidanceReadiness.Loading,
            "ready" => GuidanceReadiness.Ready,
            _ => GuidanceReadiness.Unavailable,
        };
    }

    /// <summary>The poll itself failed, so there is nothing to wait for.</summary>
    public void CorporaUnavailable()
    {
        ReadinessDetail = "";
        RefusedCorpora = [];
        Readiness = GuidanceReadiness.Unavailable;
    }

    /// <summary>A search running when the engine drops never returns, so the cards stay.</summary>
    public void ConnectionLost()
    {
        if (Searching)
        {
            Section = _noteResults.Count > 0
                ? GuidanceSection.Results
                : GuidanceSection.NotSearched;
        }

        ApplyQueryFailed();
    }

    private void ApplyRecord(JsonElement record)
    {
        _noteResults = ReadResults(record, true);
        Section = _noteResults.Count > 0 ? GuidanceSection.Results
            : Searched(record).Count > 0 ? GuidanceSection.NothingMatched
            : GuidanceSection.NoCorpusAtSearch;
        StaleCaption = NoteStaleCaption;
        Stale = Flag(record, "stale");
        ShowCards();
    }

    private void QueryChanged()
    {
        OnPropertyChanged(nameof(QueryShown));
        OnPropertyChanged(nameof(QueryHeader));
        OnPropertyChanged(nameof(QueryCaption));
        OnPropertyChanged(nameof(QueryCaptionVisible));
        OnPropertyChanged(nameof(CaptionVisible));
        ClearQueryCommand.NotifyCanExecuteChanged();
        ShowCards();
    }

    // A typed query stands in for the note's cards until it is cleared. Added
    // documents lead, whole-note matches sort after the sentences, then each
    // guideline takes one card, the first after the documents under a divider
    private void ShowCards()
    {
        Cards.Clear();
        IEnumerable<GuidanceRecommendation> shown = QueryShown
            ? _queryResults ?? []
            : _noteResults.OrderBy(r => r.Trigger.Length == 0 ? 1 : 0);
        var documents = false;
        foreach (var card in GuidanceCard.Group(shown.OrderBy(r => r.FromDocument ? 0 : 1)))
        {
            if (card.FromDocument)
            {
                documents = true;
                Cards.Add(card);
            }
            else if (documents)
            {
                documents = false;
                var from = card.Labelled ? card.SourceLabel : "installed guidance";
                Cards.Add(card with { Divider = $"From {from}" });
            }
            else
            {
                Cards.Add(card);
            }
        }

        OnPropertyChanged(nameof(CardsVisible));
        OnPropertyChanged(nameof(Summary));
        CardsShown?.Invoke();
    }

    private static string Count(int n, string noun) => GuidanceCard.Count(n, noun);

    // A search the clock never timed, such as a stored record, shows nothing
    private static string Elapsed(Stopwatch clock)
    {
        if (!clock.IsRunning)
        {
            return "";
        }

        clock.Stop();
        return $"found in {clock.Elapsed.TotalSeconds:0.0} s";
    }

    // The source label needs the corpus name, which only the searched list carries
    private static List<GuidanceRecommendation> ReadResults(JsonElement record, bool fromNote)
    {
        var sources = new Dictionary<string, (string Name, string Label)>(StringComparer.Ordinal);
        foreach (var corpus in Searched(record))
        {
            sources[GuidanceCard.Field(corpus, "id")] =
                (GuidanceCard.Field(corpus, "name"), GuidanceCard.Field(corpus, "label"));
        }

        var results = new List<GuidanceRecommendation>();
        if (record.TryGetProperty("shown", out var shown) && shown.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in shown.EnumerateArray())
            {
                var (name, label) = sources.GetValueOrDefault(
                    GuidanceCard.Field(result, "corpus"), ("", ""));
                var labelled = label.Length > 0;
                results.Add(GuidanceRecommendation.From(
                    result, labelled ? label : name, fromNote, labelled));
            }
        }

        return results;
    }

    private static List<JsonElement> Searched(JsonElement record) =>
        record.TryGetProperty("searched", out var searched)
        && searched.ValueKind == JsonValueKind.Array
            ? [.. searched.EnumerateArray()]
            : [];

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
