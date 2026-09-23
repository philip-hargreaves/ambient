using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Sessions;

public sealed record SessionRow(
    string Id, string Title, string Started, string Duration, string EditedLabel,
    string StartedAt = "", bool Demo = false, bool HasReflection = false)
{
    public bool Edited => EditedLabel.Length > 0;

    /// <summary>An unlabelled row shows the date once.</summary>
    public bool HasLabel => Title != Started;

    public string Heading => HasLabel ? Title : $"{Started} · {Duration}";

    public string Meta => HasLabel ? $"{Started} · {Duration}" : "";

    public bool MetaVisible => HasLabel || Edited;
}

/// <summary>
/// Past consultations. Selecting one opens it for review through the
/// consultation view model, so the shared panes show it and regenerate,
/// translate and save act on it.
/// </summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly IEngineApi _engine;
    private readonly StatusBarViewModel _status;
    private readonly ConsultationViewModel _consultation;
    private readonly AppPreferences? _preferences;

    public ObservableCollection<SessionRow> Sessions { get; } = [];

    /// <summary>
    /// Keep consultations is off and nothing is stored: the page explains
    /// itself instead of showing a bare empty list. Existing history always
    /// shows; only the clinician empties it.
    /// </summary>
    [ObservableProperty]
    public partial bool EmptyBecauseOff { get; private set; }

    [ObservableProperty]
    public partial SessionRow? Selected { get; set; }

    /// <summary>True while the selected session is open in the panes.</summary>
    [ObservableProperty]
    public partial bool DetailOpen { get; private set; }

    /// <summary>The open session's label; editing it renames the session.</summary>
    [ObservableProperty]
    public partial string DetailTitle { get; set; } = "";

    [ObservableProperty]
    public partial string DetailMeta { get; private set; } = "";

    public NoteViewModel Note => _consultation.Note;

    public SessionsViewModel(
        IEngineApi engine, StatusBarViewModel status, ConsultationViewModel consultation,
        AppPreferences? preferences = null)
    {
        _engine = engine;
        _status = status;
        _consultation = consultation;
        _preferences = preferences;
    }

    // True while a rename swaps the selected row for its retitled copy;
    // that reselection must not reopen the session
    private bool _retitling;

    partial void OnSelectedChanged(SessionRow? value)
    {
        if (!_retitling)
        {
            _ = OpenAsync(value);
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            var sessions = await _engine.ListSessionsAsync().ConfigureAwait(true);
            Selected = null;
            Sessions.Clear();
            foreach (var session in sessions)
            {
                var started = session.StartedAt;
                var label = session.Label ?? "";
                // No title beats a bad title: without a stored label the
                // date and time are the row's name
                var startedLabel = FormatStarted(started);
                Sessions.Add(new SessionRow(
                    session.Id,
                    label.Length > 0 ? label : startedLabel,
                    startedLabel,
                    FormatDuration(session.AudioSeconds, started, session.EndedAt),
                    EditedStamp.Label(started, session.EditedAt ?? ""),
                    started,
                    session.Demo,
                    session.HasReflection));
            }

            EmptyBecauseOff = Sessions.Count == 0
                && _preferences is { KeepConsultations: false };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status.Append($"could not list sessions: {e.Message}");
        }
    }

    private async Task OpenAsync(SessionRow? row)
    {
        if (row is null)
        {
            DetailOpen = false;
            return;
        }

        DetailOpen = await _consultation.OpenStoredSessionAsync(
                row.Id, row.Started, row.StartedAt, row.HasReflection, row.Demo)
            .ConfigureAwait(true);
        if (DetailOpen)
        {
            DetailTitle = row.Title;
            DetailMeta = $"{row.Started} · {row.Duration} · {OptionsLabel()}";
        }
    }

    /// <summary>Commits an edited title as the session's label.</summary>
    [RelayCommand]
    public async Task RenameAsync()
    {
        if (Selected is null || DetailTitle.Length == 0 || DetailTitle == Selected.Title)
        {
            return;
        }

        try
        {
            await _engine.LabelSessionAsync(Selected.Id, DetailTitle).ConfigureAwait(true);
            var index = Sessions.IndexOf(Selected);
            var renamed = Selected with { Title = DetailTitle };
            _retitling = true;
            try
            {
                Sessions[index] = renamed;  // replacing the item deselects it
                Selected = renamed;
            }
            finally
            {
                _retitling = false;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status.Append($"could not rename: {e.Message}");
        }
    }

    [RelayCommand]
    public Task DeleteSelectedAsync() => DeleteAsync(Selected);

    /// <summary>Deletes one row, closing its review first if open.</summary>
    public async Task DeleteAsync(SessionRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            if (Selected?.Id == row.Id)
            {
                await _consultation.CloseReviewAsync().ConfigureAwait(true);
                DetailOpen = false;
            }

            await _engine.DeleteSessionAsync(row.Id).ConfigureAwait(true);
            _status.Append("Session deleted");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status.Append($"could not delete session: {e.Message}");
        }
    }

    /// <summary>Leaving the page ends the review and saves any edits.</summary>
    public Task LeaveAsync()
    {
        Selected = null;
        DetailOpen = false;
        return _consultation.CloseReviewAsync();
    }

    private string OptionsLabel()
    {
        var style = Note.Style switch { "soap" => "SOAP", _ => "Prose" };
        var detail = Note.Detail switch
        {
            "concise" => "concise",
            "detailed" => "detailed",
            _ => "standard",
        };
        return $"{style}, {detail}";
    }

    private static string FormatStarted(string startedAt) =>
        DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, out var started)
            ? started.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.CurrentCulture)
            : startedAt;

    // The consultation's length is its audio, which a fast replay records in
    // seconds of wall time; the wall clock is only the fallback
    private static string FormatDuration(double audioSeconds, string startedAt, string endedAt)
    {
        var seconds = audioSeconds;
        if (seconds <= 0)
        {
            if (!DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, out var started)
                || !DateTimeOffset.TryParse(endedAt, CultureInfo.InvariantCulture, out var ended))
            {
                return "";
            }

            seconds = (ended - started).TotalSeconds;
        }

        // A duration, unmistakably not a second clock time
        return seconds < 90 ? "1 min" : $"{(int)Math.Round(seconds / 60)} min";
    }
}
