using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Appraisal;

/// <summary>
/// One appraisal reflection: the case summary the engine writes and the three answers the clinician writes. Saved only when changed.
/// </summary>
public sealed partial class ReflectionViewModel : ObservableObject
{

    private readonly IEngineApi _engine;
    private readonly StatusBarViewModel? _status;
    private readonly Action<EngineNotification> _onNotification;
    private string _savedHappened = "";
    private string _savedLearned = "";
    private string _savedNext = "";
    private string _savedTitle = "";

    public ReflectionViewModel(
        IEngineApi engine, IUiDispatcher dispatcher, StatusBarViewModel? status = null)
    {
        _engine = engine;
        _status = status;
        _onNotification = notification => dispatcher.Post(() => HandleNotification(notification));
        _engine.NotificationReceived += _onNotification;
    }

    public string SessionId { get; private set; } = "";

    /// <summary>The consultation's label; typing here renames the consultation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Title { get; set; } = "";

    /// <summary>Month precision: the exact date never leaves the device.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    public partial string Month { get; private set; } = "";

    /// <summary>The title as shown and exported: the label, or the month until there is one.</summary>
    public string DisplayTitle => Title.Trim().Length > 0 ? Title.Trim() : Month;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string Summary { get; set; } = "";

    public bool HasSummary => Summary.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RewriteSummaryCommand))]
    public partial bool SummaryPending { get; private set; }

    /// <summary>Why there is no summary, when the engine could not write one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummaryProblem))]
    public partial string SummaryProblem { get; private set; } = "";

    public bool HasSummaryProblem => SummaryProblem.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string Happened { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string Learned { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning))]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    public partial string Next { get; set; } = "";

    /// <summary>What in the text may identify the patient; empty when nothing was found.</summary>
    public string Warning => IdentifierCheck.Describe(Summary + "\n" + Happened + "\n" + Learned + "\n" + Next);

    public bool HasWarning => Warning.Length > 0;

    public bool Dirty =>
        Happened != _savedHappened || Learned != _savedLearned || Next != _savedNext;

    public ReflectionEntry Entry => new(DisplayTitle, Month, Summary, Happened, Learned, Next);

    public string ExportText => ReflectionExport.Format(Entry);

    /// <summary>Loads the stored entry; asks for a summary when none exists yet.</summary>
    public async Task LoadAsync(string sessionId, string startedAt = "")
    {
        SessionId = sessionId;
        Month = MonthLabel(startedAt);
        try
        {
            var got = await _engine.GetReflectionAsync(sessionId).ConfigureAwait(true);
            Title = _savedTitle = got.Label ?? "";
            Summary = got.Summary?.Text ?? "";
            if (got.Answers is { } answers)
            {
                Happened = _savedHappened = Answer(answers.Happened ?? "");
                Learned = _savedLearned = Answer(answers.Learned ?? "");
                Next = _savedNext = Answer(answers.Next ?? "");
            }
            else
            {
                Happened = Learned = Next = _savedHappened = _savedLearned = _savedNext = "";
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not open the reflection: {e.Message}");
            return;
        }

        if (!HasSummary)
        {
            await RequestSummaryAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRewriteSummary))]
    private Task RewriteSummary() => RequestSummaryAsync();

    private bool CanRewriteSummary() => !SummaryPending && SessionId.Length > 0;

    private async Task RequestSummaryAsync()
    {
        SummaryPending = true;
        SummaryProblem = "";
        try
        {
            await _engine.SummariseReflectionAsync(SessionId).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SummaryPending = false;
            SummaryProblem = $"No summary: {e.Message}";
        }
    }

    /// <summary>Writes the answers when they changed; a no-op otherwise.</summary>
    public async Task SaveAsync()
    {
        // Whitespace-only answers are empty: a stray line break would hide the hint and count as writing
        Happened = Answer(Happened);
        Learned = Answer(Learned);
        Next = Answer(Next);
        if (!Dirty || SessionId.Length == 0)
        {
            return;
        }

        try
        {
            await _engine.UpdateReflectionAsync(SessionId, Happened, Learned, Next).ConfigureAwait(true);
            _savedHappened = Happened;
            _savedLearned = Learned;
            _savedNext = Next;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not save the reflection: {e.Message}");
        }
    }

    /// <summary>A retitle renames the consultation itself; blank keeps the old name.</summary>
    public async Task SaveTitleAsync()
    {
        var title = Title.Trim();
        if (SessionId.Length == 0 || title.Length == 0 || title == _savedTitle)
        {
            return;
        }

        try
        {
            await _engine.LabelSessionAsync(SessionId, title).ConfigureAwait(true);
            _savedTitle = title;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not rename: {e.Message}");
        }
    }

    /// <summary>The clinician corrected the summary; kept as their wording.</summary>
    public async Task SaveSummaryAsync()
    {
        if (SessionId.Length == 0)
        {
            return;
        }

        try
        {
            await _engine.UpdateReflectionSummaryAsync(SessionId, Summary).ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not save the summary: {e.Message}");
        }
    }

    public void Detach() => _engine.NotificationReceived -= _onNotification;

    private void HandleNotification(EngineNotification notification)
    {
        switch (notification)
        {
            case ReflectionSummaryReady ready when ready.Id == SessionId:
                Summary = ready.Text;
                SummaryPending = false;
                SummaryProblem = "";
                break;
            case ReflectionSummaryFailed failed when failed.Id == SessionId:
                SummaryPending = false;
                SummaryProblem = $"No summary: {failed.Detail}";
                break;
            default:
                break;
        }
    }

    public static string MonthLabel(string startedAt)
    {
        var when = DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, out var started)
            ? started.ToLocalTime()
            : DateTimeOffset.Now;
        return when.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Answer(string text) => text.Trim().Length == 0 ? "" : text;

}
