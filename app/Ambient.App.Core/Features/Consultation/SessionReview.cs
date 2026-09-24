using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Consultation;

/// <summary>
/// The consultation under review, just sealed or reopened from the store: its documents,
/// their saves and rewrites, the translation, the reflection and the guidance search.
/// </summary>
public sealed class SessionReview
{
    private readonly IEngineApi _engine;
    private readonly IUiDispatcher _dispatcher;
    private readonly StatusBarViewModel _status;
    private readonly NoteViewModel _note;
    private readonly GuidanceViewModel _guidance;
    private readonly PageViewModel _pageView;
    private readonly TranscriptViewModel _transcript;
    private readonly IDialogService _dialogs;
    private readonly SessionRecorder _recorder;
    private readonly AppPreferences? _preferences;

    private string _finalisedStartedAt = "";

    // Bumped by every open and close, so a slow open that was overtaken applies nothing
    private int _open;
    private int _documentsGeneration;

    public SessionReview(
        IEngineApi engine, IUiDispatcher dispatcher, StatusBarViewModel status, NoteViewModel note,
        GuidanceViewModel guidance, PageViewModel pageView, TranscriptViewModel transcript,
        IDialogService dialogs, SessionRecorder recorder, AppPreferences? preferences)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _status = status;
        _note = note;
        _guidance = guidance;
        _pageView = pageView;
        _transcript = transcript;
        _dialogs = dialogs;
        _recorder = recorder;
        _preferences = preferences;
        recorder.Sealed += Sealed;
        // Editing an example makes it the clinician's text: the overlay ends, the save path applies
        note.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteViewModel.NoteEditing) && note.NoteEditing)
            {
                _originalNote = null;
            }
        };
    }

    // The stored note while an example case stands in for it, so it can come back
    private string? _originalNote;

    /// <summary>An example case is showing in place of the stored note.</summary>
    public bool ExampleShown => _originalNote is not null;

    /// <summary>The session the documents belong to, null before a stop or an open.</summary>
    public string? FinalisedSessionId { get; private set; }

    /// <summary>What the store holds, for autosaving in-place edits on leave.</summary>
    public string LoadedNote { get; set; } = "";

    public string LoadedPatient { get; set; } = "";

    /// <summary>True from a regenerate request until its pipeline settles. Keeps the rewrite out of the per-session metrics.</summary>
    public bool Regenerating { get; set; }

    /// <summary>How long added documents must stop changing before the note is searched again.</summary>
    public TimeSpan DocumentsSettle { get; set; } = TimeSpan.FromSeconds(3);

    // A stop sealed the session: the documents arriving next belong to it
    private void Sealed(string id)
    {
        FinalisedSessionId = id;
        _finalisedStartedAt = "";
        // An unkept consultation is erased on leaving, so a reflection cannot outlive it
        _note.ReflectAvailable = _preferences?.KeepConsultations != false;
        _note.HasReflection = false;
    }

    // An example case stands in as the note of a demo record: the guidance
    // search runs on it and the patient sheet can be rewritten from it
    /// <summary>
    /// A written case stands in for the note of a demo record and is searched as the
    /// note is. The stored note is kept aside and written back by
    /// <see cref="RestoreOriginalNoteAsync"/> or on leaving.
    /// </summary>
    public async Task ApplyExampleCaseAsync(DemoCase example)
    {
        if (_recorder.State != SessionState.Review || !_recorder.DemoRecord)
        {
            return;
        }

        _originalNote ??= LoadedNote;
        _note.ClinicalNoteText = example.Text;
        _status.Append($"Example case: {example.Title}");
        await SearchGuidanceAsync().ConfigureAwait(true);
    }

    /// <summary>The stored note back in place of an example, saved and searched again.</summary>
    public async Task RestoreOriginalNoteAsync()
    {
        if (_originalNote is not { } original || _recorder.State != SessionState.Review)
        {
            return;
        }

        _originalNote = null;
        _note.ClinicalNoteText = original;
        _status.Append("Original note");
        await SearchGuidanceAsync().ConfigureAwait(true);
    }

    // Leaving with an example showing puts the stored note back before the autosave
    private void DropExample()
    {
        if (_originalNote is { } original)
        {
            _originalNote = null;
            _note.ClinicalNoteText = original;
        }
    }

    // A batch of documents finishing one after another searches once, when the last has
    // landed: every change starts a new generation and only the latest one's timer acts
    public void SearchAfterDocumentsSettle()
    {
        var generation = ++_documentsGeneration;
        _ = Task.Run(async () =>
        {
            await Task.Delay(DocumentsSettle).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                if (generation == _documentsGeneration && _guidance.WantsSearchAfterDocuments)
                {
                    _ = SearchGuidanceAsync();
                }
            });
        });
    }

    // Search again on the note as it is now: an unsaved edit is saved first,
    // and a consultation opened meanwhile is left alone
    public async Task SearchGuidanceAsync()
    {
        var id = FinalisedSessionId;
        if (_recorder.State != SessionState.Review || id is null)
        {
            return;
        }

        _guidance.SearchStarted();
        if (_note.ClinicalNoteText != LoadedNote)
        {
            await SaveNoteAsync().ConfigureAwait(true);
            if (id != FinalisedSessionId)
            {
                return;
            }
        }

        if (!await EngineStep.TryAsync(_status, "guidance/search", () => _engine.SearchGuidanceAsync(id))
            .ConfigureAwait(true))
        {
            _guidance.ApplyFailed();
        }
    }

    public async Task SearchGuidanceAsync(string text)
    {
        if (!await EngineStep.TryAsync(_status, "guidance/search", () => _engine.SearchGuidanceAsync(text, 3))
            .ConfigureAwait(true))
        {
            _guidance.ApplyQueryFailed();
        }
    }

    // Results are keyed to the consultation on screen. A typed query has no id.
    // A search replaced by a newer one says so and changes nothing
    public void ApplyGuidance(GuidanceRecord record)
    {
        if (record.Detail == "superseded")
        {
            return;
        }

        if (record.Id is null)
        {
            _guidance.ApplyQueryReady(record);
            return;
        }

        if (record.Id != FinalisedSessionId)
        {
            _status.Log($"guidance for another session dropped: {record.Id}");
            return;
        }

        _guidance.ApplyReady(record);
        if (record.StoreError is { Length: > 0 } storeError)
        {
            _status.Log($"guidance not stored: {storeError}");
        }
    }

    public void ApplyGuidanceFailed(GuidanceFailed failed)
    {
        if (failed.Detail == "superseded")
        {
            return;
        }

        if (failed.Id is null)
        {
            _guidance.ApplyQueryFailed();
        }
        else if (failed.Id != FinalisedSessionId)
        {
            _status.Log($"guidance for another session dropped: {failed.Id}");
            return;
        }
        else
        {
            _guidance.ApplyFailed();
        }

        _status.Log($"guidance search failed: {failed.Detail}");
    }

    public async Task TranslateAsync(string language)
    {
        if (FinalisedSessionId is not { } id)
        {
            return;
        }

        _note.TranslationText = "";
        await EngineStep.TryAsync(_status, "patient/translate", () => _engine.TranslatePatientAsync(id, language))
            .ConfigureAwait(true);
    }

    // Opening the sheet creates the entry, so the button reads Open from here on
    public async Task ReflectAsync()
    {
        if (FinalisedSessionId is not { } id)
        {
            return;
        }

        await _dialogs.ShowReflectionAsync(id, _finalisedStartedAt).ConfigureAwait(true);
        _note.HasReflection = true;
    }

    // Unsaved note edits are saved first, so the rewrite reads what is on screen
    public async Task RegeneratePatientAsync()
    {
        if (_recorder.State != SessionState.Review)
        {
            return;
        }

        await SaveNoteAsync().ConfigureAwait(true);
        var accepted = await EngineStep.TryAsync(_status, "patient/regenerate", () => _engine.RegeneratePatientAsync())
            .ConfigureAwait(true);
        if (accepted)
        {
            Regenerating = true;
            _note.PatientInfoText = "";
            _status.Append("Rewriting patient sheet", busy: true);
        }
    }

    public async Task RegenerateNoteAsync()
    {
        if (_recorder.State != SessionState.Review)
        {
            return;
        }

        var accepted = await EngineStep.TryAsync(_status, "note/regenerate",
            () => _engine.RegenerateNoteAsync(_note.Style, _note.Detail)).ConfigureAwait(true);
        if (accepted)
        {
            _originalNote = null;  // the rewrite replaces whatever showed
            Regenerating = true;
            _note.BeginRegenerate();
            _guidance.NoteStarted();
        }
    }

    /// <summary>The clinician says it was a consultation: the note lane runs with the refusal off.</summary>
    public async Task WriteNoteAnywayAsync()
    {
        if (_recorder.State is not (SessionState.Review or SessionState.Refused))
        {
            return;
        }

        var accepted = await EngineStep.TryAsync(_status, "note/regenerate",
            () => _engine.RegenerateNoteAsync(_note.Style, _note.Detail, confirmed: true)).ConfigureAwait(true);
        if (accepted)
        {
            Regenerating = true;
            _note.BeginRegenerate();
            _guidance.NoteStarted();
            _recorder.EnterReview();  // insisted: the transcript and note are worth showing
        }
    }

    // An unchanged note is not saved: every write counts as an edit in the
    // store and would mark the sheet and the guidance stale for nothing. A
    // consultation opened during the save keeps its own stamps
    public async Task SaveNoteAsync()
    {
        var id = FinalisedSessionId;
        var text = _note.ClinicalNoteText;
        if (id is null || text == LoadedNote)
        {
            return;
        }

        var saved = await EngineStep.TryAsync(_status, "note/update", () => _engine.UpdateNoteAsync(id, text))
            .ConfigureAwait(true);
        if (saved && id == FinalisedSessionId)
        {
            LoadedNote = text;
            _note.EditedStamp = EditedStamp.Now();
            _note.PatientStale = _note.PatientInfoText.Length > 0;
            _guidance.MarkStale();
            _status.Append("Note saved");
        }
    }

    public async Task SavePatientAsync()
    {
        if (FinalisedSessionId is not { } id)
        {
            return;
        }

        var saved = await EngineStep.TryAsync(_status, "patient/update",
            () => _engine.UpdatePatientAsync(id, _note.PatientInfoText)).ConfigureAwait(true);
        if (saved)
        {
            LoadedPatient = _note.PatientInfoText;
            _status.Append("Patient note saved");
        }
    }

    /// <summary>
    /// A stored session becomes the review, as if it had just been sealed:
    /// the same panes, and regenerate, translate and save act on it. Refused
    /// while recording. Unsaved edits to the previous review are saved first.
    /// </summary>
    /// <summary>True while the review is a stored session rather than the one just recorded.</summary>
    public bool StoredOpen { get; private set; }

    public async Task<bool> OpenStoredSessionAsync(string id, string startedLabel = "",
        string startedAt = "", bool hasReflection = false, bool demo = false)
    {
        if (_recorder.State is SessionState.Recording or SessionState.Finalising)
        {
            return false;
        }

        var open = ++_open;
        await AutosaveReviewAsync().ConfigureAwait(true);
        if (!await EngineStep.TryAsync(_status, "session/open", () => _engine.OpenSessionAsync(id)).ConfigureAwait(true)
            || open != _open)
        {
            return false;
        }

        _recorder.ShowDemo(demo);
        StoredOpen = true;
        FinalisedSessionId = id;
        _finalisedStartedAt = startedAt;
        Regenerating = false;
        _note.Reset();
        _guidance.Reset();
        _pageView.Hide();
        _note.ReflectAvailable = true;  // stored, so it will still be there
        _note.HasReflection = hasReflection;
        await _recorder.LoadFinalTranscriptAsync(id).ConfigureAwait(true);

        var note = await EngineStep.TryAsync(_status, "session/note", () => _engine.StoredNoteAsync(id))
            .ConfigureAwait(true);
        var patient = await EngineStep.TryAsync(_status, "session/patient", () => _engine.StoredPatientAsync(id))
            .ConfigureAwait(true);
        if (open != _open)
        {
            return false;
        }

        var translation = patient?.Translation;
        _note.LoadStored(
            note?.Text ?? "", patient?.Text ?? "", translation?.Text ?? "",
            note?.Style ?? "", note?.Detail ?? "",
            EditedStamp.Label(note?.GeneratedAt ?? "", note?.EditedAt ?? ""),
            translation?.Language ?? "");
        LoadedNote = _note.ClinicalNoteText;
        LoadedPatient = _note.PatientInfoText;
        // ISO 8601 UTC compares as text: the sheet predates the note edit
        var noteEdited = note?.EditedAt ?? "";
        var sheetWritten = patient?.GeneratedAt ?? "";
        _note.PatientStale = noteEdited.Length > 0 && sheetWritten.Length > 0
            && string.CompareOrdinal(noteEdited, sheetWritten) > 0;
        // What this note was shown, without a model. Nothing to read for an empty note
        if (_note.ClinicalNoteText.Length > 0)
        {
            var stored = await EngineStep.TryAsync(_status, "session/guidance", () => _engine.StoredGuidanceAsync(id))
                .ConfigureAwait(true);
            if (open != _open)
            {
                return false;
            }

            // Documents added since this note was searched: refresh once the view has settled
            if (_guidance.LoadStored(stored))
            {
                SearchAfterDocumentsSettle();
            }
        }

        _recorder.EnterReview(panesOpen: true);
        _status.Append(startedLabel.Length > 0
            ? $"Reviewing the consultation from {startedLabel}"
            : "Reviewing a stored consultation");
        return true;
    }

    /// <summary>Leaves the review or a refusal: edits saved, the engine told
    /// (which deletes a refused session), panes cleared.</summary>
    public async Task CloseReviewAsync()
    {
        if (_recorder.State is not (SessionState.Review or SessionState.Refused))
        {
            return;
        }

        _open++;
        await AutosaveReviewAsync().ConfigureAwait(true);
        await EngineStep.TryAsync(_status, "session/close", () => _engine.CloseSessionAsync()).ConfigureAwait(true);
        _note.Reset();
        _guidance.Reset();
        _pageView.Hide();
        _transcript.Clear();
        Regenerating = false;
        StoredOpen = false;
        FinalisedSessionId = null;
        _finalisedStartedAt = "";
        LoadedNote = "";
        LoadedPatient = "";
        _recorder.Idle();
        _status.Append("Ready");
    }

    // In-place edits persist without a click: whatever differs from the
    // store when the clinician moves on is saved as their wording
    private async Task AutosaveReviewAsync()
    {
        if (_recorder.State != SessionState.Review || FinalisedSessionId is null)
        {
            return;
        }

        DropExample();
        if (_note.ClinicalNoteText != LoadedNote)
        {
            await SaveNoteAsync().ConfigureAwait(true);
        }

        if (_note.PatientInfoText != LoadedPatient)
        {
            await SavePatientAsync().ConfigureAwait(true);
        }
    }
}
