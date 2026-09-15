using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ambient.App.Core.ViewModels;

/// <summary>Whether the engine can search at all: the embedder and the installed corpora.</summary>
public enum GuidanceReadiness
{
    Loading,
    Ready,
    NoCorpus,
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
/// engine and feeds this from the wire; nothing here talks to it.
/// </summary>
public sealed partial class GuidanceViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateCaption), nameof(CaptionVisible),
        nameof(SettingsLinkVisible),
        nameof(QueryBoxEnabled), nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchNoteCommand), nameof(SearchQueryCommand))]
    public partial GuidanceReadiness Readiness { get; private set; } = GuidanceReadiness.Loading;

    /// <summary>The loader's reason when unavailable, for the log; never shown.</summary>
    public string ReadinessDetail { get; private set; } = "";

    /// <summary>Corpora the engine refused, as "id: reason", for the log.</summary>
    public IReadOnlyList<string> RefusedCorpora { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible), nameof(Searching), nameof(Failed),
        nameof(NotSearched), nameof(HasRecord), nameof(StateCaption), nameof(CaptionVisible),
        nameof(SearchAgainVisible), nameof(QueryBoxEnabled), nameof(SearchEnabled))]
    [NotifyCanExecuteChangedFor(nameof(SearchNoteCommand), nameof(SearchQueryCommand))]
    public partial GuidanceSection Section { get; private set; } = GuidanceSection.Hidden;

    /// <summary>The note has been written since this guidance was found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchAgainVisible))]
    public partial bool Stale { get; private set; }

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

    /// <summary>A typed query's cards while one shows; otherwise the note's.</summary>
    public ObservableCollection<GuidanceCard> Cards { get; } = [];

    /// <summary>Set by the consultation view model, which owns the engine.</summary>
    public Func<Task>? SearchNoteRequested { get; set; }

    public Func<string, Task>? SearchQueryRequested { get; set; }

    private List<GuidanceRecommendation> _noteResults = [];
    private List<GuidanceRecommendation>? _queryResults;
    private string _queryText = "";

    public bool Visible => Section != GuidanceSection.Hidden;

    public bool Searching => Section == GuidanceSection.Searching;

    public bool Failed => Section == GuidanceSection.Failed;

    public bool NotSearched => Section == GuidanceSection.NotSearched;

    public bool SettingsLinkVisible =>
        Readiness is GuidanceReadiness.NoCorpus or GuidanceReadiness.Unavailable;

    public bool HasRecord => Section is GuidanceSection.Results
        or GuidanceSection.NothingMatched or GuidanceSection.NoCorpusAtSearch;

    public bool CardsVisible => Cards.Count > 0;

    public bool LimitationVisible => Cards.Count > 0;

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
            return $"{Count(Cards.Count, "guideline")} · {Count(found, "recommendation")}";
        }
    }

    public bool SearchAgainVisible => Stale && !Searching;

    /// <summary>The box waits while either search runs; the button also needs a query.</summary>
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
            + "The installed guidance may still cover this condition.",
        GuidanceSection.NoCorpusAtSearch =>
            "No guidance was installed when this note was searched.",
        GuidanceSection.Failed => "Guidance could not be searched.",
        _ when Readiness != GuidanceReadiness.Ready => Readiness switch
        {
            GuidanceReadiness.Loading => "Loading the guidance model",
            GuidanceReadiness.NoCorpus => "No guidance is installed on this device.",
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
        QueryChanged();
        return SearchQueryRequested?.Invoke(_queryText) ?? Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(QueryShown))]
    private void ClearQuery()
    {
        _queryText = "";
        _queryResults = null;
        QuerySearching = false;
        QueryFailed = false;
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

    /// <summary>The note is being written; its search follows.</summary>
    public void NoteStarted()
    {
        _noteResults = [];
        Stale = false;
        NotStored = false;
        Section = GuidanceSection.FollowsNote;
        ShowCards();
    }

    /// <summary>The note arrived; a result or failure that beat it stands.</summary>
    public void NoteReady()
    {
        if (Section == GuidanceSection.FollowsNote)
        {
            Section = GuidanceSection.Searching;
        }
    }

    public void NoteFailed() => Section = GuidanceSection.Hidden;

    /// <summary>The consultation view model has accepted a search-again request.</summary>
    public void SearchStarted()
    {
        Stale = false;
        Section = GuidanceSection.Searching;
    }

    public void MarkStale()
    {
        if (HasRecord)
        {
            Stale = true;
        }
    }

    /// <summary>A reopened session's record, or null when it was never searched.</summary>
    public void LoadStored(JsonElement? guidance)
    {
        NotStored = false;
        if (guidance is not { ValueKind: JsonValueKind.Object } record)
        {
            _noteResults = [];
            Stale = false;
            Section = GuidanceSection.NotSearched;
            ShowCards();
            return;
        }

        ApplyRecord(record);
        Stale = Flag(record, "stale");
    }

    /// <summary>The note's search came back, for the consultation on screen.</summary>
    public void ApplyReady(JsonElement result)
    {
        ApplyRecord(result);
        Stale = Flag(result, "stale");
        NotStored = GuidanceCard.Field(result, "storeError").Length > 0;
    }

    public void ApplyFailed() => Section = GuidanceSection.Failed;

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

    /// <summary>guidance/corpora: the embedder's state and what it can search.</summary>
    public void ApplyCorpora(JsonElement corpora)
    {
        ReadinessDetail = GuidanceCard.Field(corpora, "detail");
        var refused = new List<string>();
        var loaded = 0;
        if (corpora.TryGetProperty("corpora", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var corpus in list.EnumerateArray())
            {
                var reason = GuidanceCard.Field(corpus, "unavailable");
                if (reason.Length > 0)
                {
                    refused.Add($"{GuidanceCard.Field(corpus, "id")}: {reason}");
                }
                else
                {
                    loaded++;
                }
            }
        }

        RefusedCorpora = refused;
        Readiness = GuidanceCard.Field(corpora, "state") switch
        {
            "loading" => GuidanceReadiness.Loading,
            "ready" => loaded > 0 ? GuidanceReadiness.Ready : GuidanceReadiness.NoCorpus,
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

    /// <summary>Searches in flight when the engine went never finish; the cards stay.</summary>
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
            : SearchedCount(record) > 0 ? GuidanceSection.NothingMatched
            : GuidanceSection.NoCorpusAtSearch;
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

    // A typed query stands in for the note's cards until it is cleared. Whole-note
    // matches sort after the sentences, then each guideline takes one card
    private void ShowCards()
    {
        Cards.Clear();
        IEnumerable<GuidanceRecommendation> shown = QueryShown
            ? _queryResults ?? []
            : _noteResults.OrderBy(r => r.Trigger.Length == 0 ? 1 : 0);
        foreach (var card in GuidanceCard.Group(shown))
        {
            Cards.Add(card);
        }

        OnPropertyChanged(nameof(CardsVisible));
        OnPropertyChanged(nameof(LimitationVisible));
        OnPropertyChanged(nameof(Summary));
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    // The source label needs the corpus name, which only the searched list carries
    private static List<GuidanceRecommendation> ReadResults(JsonElement record, bool fromNote)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var corpus in Searched(record))
        {
            names[GuidanceCard.Field(corpus, "id")] = GuidanceCard.Field(corpus, "name");
        }

        var results = new List<GuidanceRecommendation>();
        if (record.TryGetProperty("shown", out var shown) && shown.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in shown.EnumerateArray())
            {
                var label = GuidanceCard.Field(result, "source") == "nice" ? "NICE"
                    : names.GetValueOrDefault(GuidanceCard.Field(result, "corpus"), "");
                results.Add(GuidanceRecommendation.From(result, label, fromNote));
            }
        }

        return results;
    }

    private static List<JsonElement> Searched(JsonElement record) =>
        record.TryGetProperty("searched", out var searched)
        && searched.ValueKind == JsonValueKind.Array
            ? [.. searched.EnumerateArray()]
            : [];

    private static int SearchedCount(JsonElement record) => Searched(record).Count;

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
