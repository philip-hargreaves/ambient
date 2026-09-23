using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Consultation;

public sealed partial class ConsultationViewModel : ObservableObject, ISessionState
{

    // Accelerated replay legitimately leaves a decode backlog for stop to
    // drain; at 1x this is seconds

    private readonly IEngineApi _engine;
    private readonly IUiDispatcher _dispatcher;
    private int _documentsGeneration;
    private readonly Metrics.PerformanceCollector? _metrics;
    private readonly AppPreferences? _preferences;

    [ObservableProperty]
    public partial SessionState State { get; private set; } = SessionState.Idle;

    /// <summary>False while the engine is still starting or reconnecting.</summary>
    [ObservableProperty]
    public partial bool EngineReady { get; private set; }

    [ObservableProperty]
    public partial bool Paused { get; private set; }

    // Delivered-audio position: one level event per 100 ms of audio, at any
    // replay speed
    [ObservableProperty]
    public partial double AudioSeconds { get; private set; }

    /// <summary>The active session's replay request; null for a microphone.</summary>
    [ObservableProperty]
    public partial ReplayRequest? ActiveReplay { get; private set; }

    /// <summary>The saved run being played back; null unless a demo plays.</summary>
    [ObservableProperty]
    public partial DemoMaster? ActivePlayback { get; private set; }

    // The badge shows for demo mode, and for a demo record until the next idle
    private bool _demoRecord;

    private void ShowDemo(bool record)
    {
        _demoRecord = record;
        Status.Demo = record || _demo is { Enabled: true };
        Note.ExampleCasesVisible = record && Note.ExampleCases.Count > 0;
        if (!record)
        {
            Note.ExampleCaseIndex = -1;
        }
    }

    // An example case stands in as the note of a demo record: the guidance
    // search runs on it and the patient sheet can be rewritten from it
    private async Task ApplyExampleCaseAsync(DemoCase example)
    {
        if (State != SessionState.Review || !_demoRecord)
        {
            return;
        }

        Note.ClinicalNoteText = example.Text;
        Status.Append($"Example case: {example.Title}");
        await SearchGuidanceAsync().ConfigureAwait(true);
    }

    partial void OnStateChanged(SessionState value)
    {
        if (value == SessionState.Idle)
        {
            ShowDemo(false);
        }
    }

    /// <summary>
    /// Where a stop has got to. The centre stage holds until the note streams,
    /// so the pause of the note prefill is spent on a spinner that says so,
    /// not on an empty document. Advances only forwards within one stop.
    /// </summary>
    [ObservableProperty]
    public partial FinalisePhase Phase { get; private set; } = FinalisePhase.None;

    /// <summary>
    /// False only during first-time setup, while the one-off model compiles
    /// run: recording is blocked so nobody's first impression is the slow
    /// path. Warm launches are never gated.
    /// </summary>
    [ObservableProperty]
    public partial bool ModelsReady { get; private set; } = true;

    public bool ConsultationActive => State != SessionState.Idle;

    /// <summary>Finalising is the state worth splitting: its stages differ
    /// by an order of magnitude, so a crash log needs which one.</summary>
    public string SessionPhase =>
        State == SessionState.Finalising ? $"{State}:{Phase}" : State.ToString();

    public TranscriptViewModel Transcript { get; }

    public NoteViewModel Note { get; }

    public GuidanceViewModel Guidance { get; }

    /// <summary>The page of an added document beside the note, when a card asks.</summary>
    public PageViewModel PageView { get; }

    public StatusBarViewModel Status { get; }

    public ConsultationViewModel(
        IEngineApi engine, IUiDispatcher dispatcher,
        TranscriptViewModel transcript, NoteViewModel note, StatusBarViewModel status,
        IDialogService dialogs, PageViewModel pageView, GuidanceViewModel guidance,
        Metrics.PerformanceCollector? metrics = null, TimeSpan? readinessPollInterval = null,
        AppPreferences? preferences = null, DemoMode? demo = null)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        PageView = pageView;
        _metrics = metrics;
        _preferences = preferences;
        _demo = demo;
        _readinessPollInterval = readinessPollInterval ?? TimeSpan.FromSeconds(2);
        Transcript = transcript;
        Note = note;
        Guidance = guidance;
        Status = status;
        if (demo is not null)
        {
            demo.Changed += () => dispatcher.Post(() => ShowDemo(_demoRecord));
            Status.Demo = demo.Enabled;
        }

        EngineReady = engine.Connected;
        Note.TranslateRequested = TranslateAsync;
        Note.RegenerateRequested = RegenerateNoteAsync;
        Note.WriteAnywayRequested = WriteNoteAnywayAsync;
        Note.RegeneratePatientRequested = RegeneratePatientAsync;
        Note.ReflectRequested = ReflectAsync;
        Note.SaveNoteRequested = SaveNoteAsync;
        Note.SavePatientRequested = SavePatientAsync;
        Note.ExampleCases = demo?.Cases ?? [];
        Note.ExampleCaseRequested = example => _ = ApplyExampleCaseAsync(example);
        Guidance.SearchNoteRequested = SearchGuidanceAsync;
        Guidance.SearchQueryRequested = SearchGuidanceAsync;
        Guidance.ShowInDocumentRequested = PageView.ShowAsync;
        Guidance.OpenDocumentRequested = PageView.OpenAsync;
        Guidance.CardsShown = () => PageView.KeepOnlyFor(Guidance.Cards);
        // Persisted options applied before the change callback is wired,
        // so restoring them is not itself a change
        if (preferences is not null)
        {
            Note.Style = preferences.NoteStyle;
            Note.Detail = preferences.NoteDetail;
        }

        Note.OptionsChanged = OnNoteOptionsChanged;
        _engine.NotificationReceived +=
            notification => dispatcher.Post(() => HandleNotification(notification));
        // The status-bar label carries readiness; only the loss is log-worthy
        _engine.ConnectedChanged += connected => dispatcher.Post(() =>
        {
            EngineReady = connected;
            Status.SetEngineReady(connected);
            if (!connected)
            {
                // An intentional restart (the NPU switch) must not read as a failure
                Note.TranslationRunning = false;
                Guidance.ConnectionLost();
                Status.Log("connection lost");
            }
            else
            {
                // Whatever activity a restart interrupted ("switching
                // transcription...") is over; resume overwrites this
                Status.Append("Ready");
                _ = LoadLanguagesAsync();
                _ = LoadGuidanceReadinessAsync();
                _ = ConfigureThenCheckReadinessAsync();
                if (State == SessionState.Recording)
                {
                    _ = ResumeAfterRestartAsync();
                }
            }
        });
        if (EngineReady)
        {
            _ = LoadLanguagesAsync();
            _ = LoadGuidanceReadinessAsync();
            _ = ConfigureThenCheckReadinessAsync();
        }
    }

    // Polled rather than only listened for: the embedder loads before the
    // shell connects, so its notification can be missed
    private async Task LoadGuidanceReadinessAsync()
    {
        var before = (Guidance.Readiness, Guidance.ReadinessDetail, Guidance.RefusedCorpora.Count);
        try
        {
            Guidance.ApplyCorpora(await _engine.GuidanceCorporaAsync().ConfigureAwait(true));
        }
        catch (Exception e)
        {
            Guidance.CorporaUnavailable();
            Status.Log($"guidance/corpora failed: {e.Message}");
        }

        // Logged on change only: the poll repeats on every model transition
        if ((Guidance.Readiness, Guidance.ReadinessDetail, Guidance.RefusedCorpora.Count) == before)
        {
            return;
        }

        if (Guidance.ReadinessDetail.Length > 0)
        {
            Status.Log($"guidance unavailable: {Guidance.ReadinessDetail}");
        }

        foreach (var refused in Guidance.RefusedCorpora)
        {
            Status.Log($"guidance corpus refused: {refused}");
        }
    }

    // Search again on the note as it is now: an unsaved edit is saved first,
    // and a consultation opened meanwhile is left alone
    /// <summary>How long added documents must stop changing before the note is searched again.</summary>
    public TimeSpan DocumentsSettle { get; set; } = TimeSpan.FromSeconds(3);

    // A batch of documents finishing one after another searches once, when the last has
    // landed: every change starts a new generation and only the latest one's timer acts
    private void SearchAfterDocumentsSettle()
    {
        var generation = ++_documentsGeneration;
        _ = Task.Run(async () =>
        {
            await Task.Delay(DocumentsSettle).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                if (generation == _documentsGeneration && Guidance.WantsSearchAfterDocuments)
                {
                    _ = SearchGuidanceAsync();
                }
            });
        });
    }

    private async Task SearchGuidanceAsync()
    {
        var id = _finalisedSessionId;
        if (State != SessionState.Review || id is null)
        {
            return;
        }

        Guidance.SearchStarted();
        if (Note.ClinicalNoteText != _loadedNote)
        {
            await SaveNoteAsync().ConfigureAwait(true);
            if (id != _finalisedSessionId)
            {
                return;
            }
        }

        if (!await TryAsync("guidance/search", () => _engine.SearchGuidanceAsync(id)).ConfigureAwait(true))
        {
            Guidance.ApplyFailed();
        }
    }

    private async Task SearchGuidanceAsync(string text)
    {
        if (!await TryAsync("guidance/search", () => _engine.SearchGuidanceAsync(text, 3))
            .ConfigureAwait(true))
        {
            Guidance.ApplyQueryFailed();
        }
    }

    // Results are keyed to the consultation on screen; a typed query has no id.
    // A search replaced by a newer one says so and changes nothing
    private void ApplyGuidance(GuidanceRecord record)
    {
        if (record.Detail == "superseded")
        {
            return;
        }

        if (record.Id is null)
        {
            Guidance.ApplyQueryReady(record);
            return;
        }

        if (record.Id != _finalisedSessionId)
        {
            Status.Log($"guidance for another session dropped: {record.Id}");
            return;
        }

        Guidance.ApplyReady(record);
        if (record.StoreError is { Length: > 0 } storeError)
        {
            Status.Log($"guidance not stored: {storeError}");
        }
    }

    private void ApplyGuidanceFailed(GuidanceFailed failed)
    {
        if (failed.Detail == "superseded")
        {
            return;
        }

        if (failed.Id is null)
        {
            Guidance.ApplyQueryFailed();
        }
        else if (failed.Id != _finalisedSessionId)
        {
            Status.Log($"guidance for another session dropped: {failed.Id}");
            return;
        }
        else
        {
            Guidance.ApplyFailed();
        }

        Status.Log($"guidance search failed: {failed.Detail}");
    }

    // Tier before readiness: readiness reports the configured tier's compile cache
    private async Task ConfigureThenCheckReadinessAsync()
    {
        await PushNoteOptionsAsync().ConfigureAwait(true);
        await CheckReadinessAsync().ConfigureAwait(true);
    }

    private readonly TimeSpan _readinessPollInterval;

    // First launch only: poll until the one-off compiles finish, then never
    // again. Fails open - a readiness error must not brick recording.
    private async Task CheckReadinessAsync()
    {
        try
        {
            var readiness = await _engine.ReadinessAsync().ConfigureAwait(true);
            // A note host wedged in the GPU driver outlives the engine; only a reboot ends it
            if (readiness.StrayNoteHost)
            {
                Status.Append("A previous note process is stuck in the graphics driver - restart the computer");
                Status.Log("stray note host detected at engine start");
            }

            if (!readiness.FirstUse)
            {
                ModelsReady = true;
                return;
            }

            while (!readiness.Ready)
            {
                if (ModelsReady)
                {
                    ModelsReady = false;
                    Status.Append("First-time setup - this can take a few minutes", busy: true);
                }

                await Task.Delay(_readinessPollInterval).ConfigureAwait(true);
                readiness = await _engine.ReadinessAsync().ConfigureAwait(true);
            }

            if (!ModelsReady)
            {
                ModelsReady = true;
                Status.Append("Ready");
            }
        }
        catch (Exception)
        {
            ModelsReady = true;
        }
    }

    // Empty when the engine ships without a translation model
    private async Task LoadLanguagesAsync()
    {
        try
        {
            var languages = await _engine.LanguagesAsync().ConfigureAwait(true);
            Note.Languages.Clear();
            foreach (var language in languages)
            {
                Note.Languages.Add(language);
            }
        }
        catch (Exception)
        {
        }
    }

    public async Task TranslateAsync(string language)
    {
        if (_finalisedSessionId is null)
        {
            return;
        }

        var id = _finalisedSessionId;
        Note.TranslationText = "";
        await TryAsync("patient/translate", () => _engine.TranslatePatientAsync(id, language))
            .ConfigureAwait(true);
    }

    private string? _finalisedSessionId;
    private string _finalisedStartedAt = "";
    private readonly DemoMode? _demo;
    private readonly IDialogService _dialogs;

    // Opening the sheet creates the entry, so the button reads Open from here on
    private async Task ReflectAsync()
    {
        if (_finalisedSessionId is null)
        {
            return;
        }

        await _dialogs.ShowReflectionAsync(_finalisedSessionId, _finalisedStartedAt)
            .ConfigureAwait(true);
        Note.HasReflection = true;
    }
    private string? _recordingSessionId;

    // What the store holds, for autosaving in-place edits on leave
    private string _loadedNote = "";
    private string _loadedPatient = "";

    // True from a regenerate request until its pipeline settles; keeps the
    // rewrite out of the per-session metrics
    private bool _regenerating;

    private void OnNoteOptionsChanged()
    {
        if (_preferences is not null)
        {
            _preferences.NoteStyle = Note.Style;
            _preferences.NoteDetail = Note.Detail;
            _preferences.Save();
        }

        _ = PushNoteOptionsAsync();
    }

    // Engine options are per process: resent after a restart. The tier is a
    // role; the engine's store resolves it
    private async Task PushNoteOptionsAsync()
    {
        if (_engine.Connected)
        {
            await TryAsync("note/tier", () => _engine.SetNoteTierAsync(_preferences?.NoteTier ?? "default"))
                .ConfigureAwait(true);
            await TryAsync("note/options", () => _engine.SetNoteOptionsAsync(Note.Style, Note.Detail))
                .ConfigureAwait(true);
        }
    }

    // Unsaved note edits are saved first, so the rewrite reads what is on screen
    public async Task RegeneratePatientAsync()
    {
        if (State != SessionState.Review)
        {
            return;
        }

        await SaveNoteAsync().ConfigureAwait(true);
        var accepted = await TryAsync("patient/regenerate", () => _engine.RegeneratePatientAsync())
            .ConfigureAwait(true);
        if (accepted)
        {
            _regenerating = true;
            Note.PatientInfoText = "";
            Status.Append("Rewriting patient sheet", busy: true);
        }
    }

    public async Task RegenerateNoteAsync()
    {
        if (State != SessionState.Review)
        {
            return;
        }

        var accepted = await TryAsync(
            "note/regenerate", () => _engine.RegenerateNoteAsync(Note.Style, Note.Detail))
            .ConfigureAwait(true);
        if (accepted)
        {
            _regenerating = true;
            Note.BeginRegenerate();
            Guidance.NoteStarted();
        }
    }

    /// <summary>The clinician says it was a consultation: the note lane runs with the refusal off.</summary>
    public async Task WriteNoteAnywayAsync()
    {
        if (State is not (SessionState.Review or SessionState.Refused))
        {
            return;
        }

        var accepted = await TryAsync("note/regenerate",
            () => _engine.RegenerateNoteAsync(Note.Style, Note.Detail, confirmed: true))
            .ConfigureAwait(true);
        if (accepted)
        {
            _regenerating = true;
            Note.BeginRegenerate();
            Guidance.NoteStarted();
            State = SessionState.Review;  // insisted: the transcript and note are worth showing
        }
    }

    // An unchanged note is not saved: every write counts as an edit in the
    // store and would mark the sheet and the guidance stale for nothing. A
    // consultation opened during the save keeps its own stamps
    public async Task SaveNoteAsync()
    {
        var id = _finalisedSessionId;
        var text = Note.ClinicalNoteText;
        if (id is null || text == _loadedNote)
        {
            return;
        }

        var saved = await TryAsync("note/update", () => _engine.UpdateNoteAsync(id, text))
            .ConfigureAwait(true);
        if (saved && id == _finalisedSessionId)
        {
            _loadedNote = text;
            Note.EditedStamp = EditedStamp.Now();
            Note.PatientStale = Note.PatientInfoText.Length > 0;
            Guidance.MarkStale();
            Status.Append("Note saved");
        }
    }

    public async Task SavePatientAsync()
    {
        if (_finalisedSessionId is not { } id)
        {
            return;
        }

        var saved = await TryAsync(
            "patient/update", () => _engine.UpdatePatientAsync(id, Note.PatientInfoText))
            .ConfigureAwait(true);
        if (saved)
        {
            _loadedPatient = Note.PatientInfoText;
            Status.Append("Patient note saved");
        }
    }

    /// <summary>
    /// A stored session becomes the review, as if it had just been sealed:
    /// the same panes, and regenerate, translate and save act on it. Refused
    /// while recording. Unsaved edits to the previous review are saved first.
    /// </summary>
    public async Task<bool> OpenStoredSessionAsync(string id, string startedLabel = "",
        string startedAt = "", bool hasReflection = false, bool demo = false)
    {
        if (State is SessionState.Recording or SessionState.Finalising)
        {
            return false;
        }

        await AutosaveReviewAsync().ConfigureAwait(true);
        if (!await TryAsync("session/open", () => _engine.OpenSessionAsync(id)).ConfigureAwait(true))
        {
            return false;
        }

        ShowDemo(demo);
        _finalisedSessionId = id;
        _finalisedStartedAt = startedAt;
        _recordingSessionId = null;
        _regenerating = false;
        Note.Reset();
        Guidance.Reset();
        PageView.Hide();
        Note.ReflectAvailable = true;  // stored, so it will still be there
        Note.HasReflection = hasReflection;
        await LoadFinalTranscriptAsync(id).ConfigureAwait(true);

        var note = await TryAsync("session/note", () => _engine.StoredNoteAsync(id)).ConfigureAwait(true);
        var patient = await TryAsync("session/patient", () => _engine.StoredPatientAsync(id))
            .ConfigureAwait(true);
        var translation = patient?.Translation;
        Note.LoadStored(
            note?.Text ?? "", patient?.Text ?? "", translation?.Text ?? "",
            note?.Style ?? "", note?.Detail ?? "",
            EditedStamp.Label(note?.GeneratedAt ?? "", note?.EditedAt ?? ""),
            translation?.Language ?? "");
        _loadedNote = Note.ClinicalNoteText;
        _loadedPatient = Note.PatientInfoText;
        // ISO 8601 UTC compares as text: the sheet predates the note edit
        var noteEdited = note?.EditedAt ?? "";
        var sheetWritten = patient?.GeneratedAt ?? "";
        Note.PatientStale = noteEdited.Length > 0 && sheetWritten.Length > 0
            && string.CompareOrdinal(noteEdited, sheetWritten) > 0;
        // What this note was shown, without a model; nothing to read for an empty note
        if (Note.ClinicalNoteText.Length > 0)
        {
            var stored = await TryAsync("session/guidance", () => _engine.StoredGuidanceAsync(id))
                .ConfigureAwait(true);
            // Documents added since this note was searched: refresh once the view has settled
            if (Guidance.LoadStored(stored))
            {
                SearchAfterDocumentsSettle();
            }
        }

        Phase = FinalisePhase.Streaming;  // the panes show, no centre spinner
        State = SessionState.Review;
        Status.Append(startedLabel.Length > 0
            ? $"Reviewing the consultation from {startedLabel}"
            : "Reviewing a stored consultation");
        return true;
    }


    /// <summary>Leaves the review or a refusal: edits saved, the engine told
    /// (which deletes a refused session), panes cleared.</summary>
    public async Task CloseReviewAsync()
    {
        if (State is not (SessionState.Review or SessionState.Refused))
        {
            return;
        }

        await AutosaveReviewAsync().ConfigureAwait(true);
        await TryAsync("session/close", () => _engine.CloseSessionAsync()).ConfigureAwait(true);
        Note.Reset();
        Guidance.Reset();
        PageView.Hide();
        Transcript.Clear();
        Phase = FinalisePhase.None;
        _regenerating = false;
        _finalisedSessionId = null;
        _finalisedStartedAt = "";
        _loadedNote = "";
        _loadedPatient = "";
        State = SessionState.Idle;
        Status.Append("Ready");
    }

    // In-place edits persist without a click: whatever differs from the
    // store when the clinician moves on is saved as their wording
    private async Task AutosaveReviewAsync()
    {
        if (State != SessionState.Review || _finalisedSessionId is null)
        {
            return;
        }

        if (Note.ClinicalNoteText != _loadedNote)
        {
            await SaveNoteAsync().ConfigureAwait(true);
        }

        if (Note.PatientInfoText != _loadedPatient)
        {
            await SavePatientAsync().ConfigureAwait(true);
        }
    }

    // A restarted engine lost the live session, but its audio is stored:
    // resume replays it into a fresh session and recording carries on
    private async Task ResumeAfterRestartAsync()
    {
        var resume = _recordingSessionId;
        if (resume is null)
        {
            return;
        }

        // A demo has no audio to resume from
        if (ActivePlayback is not null)
        {
            State = SessionState.Idle;
            ActivePlayback = null;
            Status.SetMicVisible(false);
            Status.Append("Playback interrupted");
            return;
        }

        try
        {
            var retain = _preferences?.KeepConsultations ?? true;
            // The raw call tells a lost engine from one that answered
            var started = await _engine.ResumeSessionAsync(resume, retain, ActiveReplay)
                .ConfigureAwait(true);
            _recordingSessionId = started.Length > 0 ? started : null;
            Status.Append("Recording");
        }
        catch (Exception e) when (e is EngineErrorException or OperationCanceledException)
        {
            // The engine is up and cannot resume, or never answered
            State = SessionState.Idle;
            Status.SetMicVisible(false);
            Status.Append("Could not resume - session kept");
            Status.Log($"session/start failed: {e.Message}");
        }
        catch (Exception)
        {
            // Died again mid-resume: stay Recording so the next reconnect retries
            Status.Append("Recovering", busy: true);
        }
    }

    public async Task StartRecordingAsync(ReplayRequest? replay = null)
    {
        if (State != SessionState.Idle)
        {
            return;
        }

        // Demo mode: the record button plays the chosen saved run back
        if (replay is null && _demo is { Enabled: true } && _demo.Master is { } master)
        {
            await StartPlaybackAsync(master).ConfigureAwait(true);
            return;
        }

        // Keep consultations off: the engine erases the session once it is left.
        // An empty mic id means the default; one that has gone falls back there, logged
        var retain = _preferences?.KeepConsultations ?? true;
        var start = replay is null
            ? () => _engine.StartSessionAsync(retain, _preferences?.MicId ?? "")
            : (Func<Task<string>>)(() => _engine.StartReplayAsync(retain, replay));
        if (!await BeginAsync(start, replay, null).ConfigureAwait(true))
        {
            return;
        }

        Status.Append(replay is null ? "Recording" : "Replaying");
        _metrics?.SessionStarted(
            replay is null ? "mic" : "replay", replay?.Speed ?? 0,
            replay is null ? null : Path.GetFileNameWithoutExtension(replay.Path));
    }

    /// <summary>
    /// A stored consultation played back as a demo: the same states, sped up,
    /// nothing generated. Never a performance measurement.
    /// </summary>
    public async Task StartPlaybackAsync(DemoMaster playback)
    {
        if (State != SessionState.Idle)
        {
            return;
        }

        var start = () => _engine.StartPlaybackAsync(playback.SessionId);
        if (!await BeginAsync(start, null, playback).ConfigureAwait(true))
        {
            return;
        }

        ShowDemo(true);
        Status.Append("Recording");
    }

    private async Task<bool> BeginAsync(
        Func<Task<string>> start, ReplayRequest? replay, DemoMaster? playback)
    {
        var started = await TryAsync("session/start", start).ConfigureAwait(true);
        if (started is null)
        {
            return false;
        }

        _recordingSessionId = started.Length > 0 ? started : null;
        Paused = false;
        AudioSeconds = 0;
        Phase = FinalisePhase.None;
        ActiveReplay = replay;
        ActivePlayback = playback;
        State = SessionState.Recording;
        Status.ResetThroughput();
        Status.SetMicVisible(true);
        return true;
    }

    public async Task SetPausedAsync(bool paused)
    {
        if (State != SessionState.Recording || paused == Paused)
        {
            return;
        }

        if (await TryAsync("session/pause", () => _engine.PauseSessionAsync(paused)).ConfigureAwait(true))
        {
            Paused = paused;
        }
    }

    public async Task SetMonitorAsync(bool on)
    {
        if (State == SessionState.Recording)
        {
            await TryAsync("session/monitor", () => _engine.MonitorSessionAsync(on)).ConfigureAwait(true);
        }
    }

    public async Task StopRecordingAsync()
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        State = SessionState.Finalising;
        Phase = FinalisePhase.Sealing;
        Paused = false;
        ActiveReplay = null;
        // A playback's timings are staged, so the collector never sees them
        if (ActivePlayback is null)
        {
            _metrics?.StopRequested();
        }

        ActivePlayback = null;
        Status.SetMicVisible(false);
        Status.SetDecodeActive(true);  // the tail decode keeps the RT figure up
        Note.Apply(NotePipelineEvent.NoteWritingStarted);
        Guidance.NoteStarted();
        Status.Append("Finalising", busy: true);
        var stopped = await TryAsync("session/stop", () => _engine.StopSessionAsync()).ConfigureAwait(true);
        if (stopped is null)
        {
            // A failed stop must not wedge the UI; the recording is safe in
            // the store either way
            State = SessionState.Idle;
            Note.Reset();
            Guidance.Reset();
            PageView.Hide();
            Status.SetDecodeActive(false);
            Status.Append("Stop failed - session kept");
            return;
        }

        // The finalised transcript carries the speaker labels the live feed
        // could not; it replaces the pane once the engine has sealed it
        if (stopped.Length > 0)
        {
            _finalisedSessionId = stopped;
            _finalisedStartedAt = "";
            // An unkept consultation is erased on leaving; a reflection cannot outlive it
            Note.ReflectAvailable = _preferences?.KeepConsultations != false;
            Note.HasReflection = false;
            await LoadFinalTranscriptAsync(_finalisedSessionId).ConfigureAwait(true);
        }
    }

    private async Task LoadFinalTranscriptAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            Status.SetDecodeActive(false);
            Phase = FinalisePhase.Note;  // nothing to fetch; the panes still open
            return;
        }

        try
        {
            var turns = await _engine.TranscriptAsync(id).ConfigureAwait(true);
            Transcript.Turns.Clear();
            foreach (var turn in turns)
            {
                Transcript.Add(turn.Speaker, turn.FirstFrame, turn.Text);
            }
        }
        catch (Exception)
        {
            Status.Append("Could not load transcript");
        }
        finally
        {
            Status.SetDecodeActive(false);  // sealed: the tail decode is over
            // A note that began streaming during the fetch keeps its panes
            if (Phase < FinalisePhase.Note)
            {
                Phase = FinalisePhase.Note;
            }
        }
    }

    public async Task CancelRecordingAsync()
    {
        if (State != SessionState.Recording)
        {
            return;
        }

        if (!await TryAsync("session/cancel", () => _engine.CancelSessionAsync()).ConfigureAwait(true))
        {
            return;
        }

        State = SessionState.Idle;
        Paused = false;
        ActiveReplay = null;
        ActivePlayback = null;
        Status.SetMicVisible(false);
        Status.Append("Cancelled");
    }

    public void StartNewConsultation() => _ = CloseReviewAsync();

    private void HandleNotification(EngineNotification notification)
    {
        if (notification is NoteModelState { State: "ready" } resident)
        {
            _metrics?.NoteModel(resident.Name, resident.Tier, resident.Seconds);
        }

        switch (notification)
        {
            // A warm model load never blocks recording; only the first-use compile does
            case NoteModelState model when State == SessionState.Idle:
                if (model.State == "loading" && model.FirstUse)
                {
                    ModelsReady = false;
                    Status.Append("Preparing note model for this computer - this can take a few minutes",
                        busy: true);
                }
                else if (model.State is "ready" or "failed" && !ModelsReady)
                {
                    ModelsReady = true;
                    Status.Append("Ready");
                }

                break;
            // Stages the engine skips never show; a late stage cannot move the
            // phase backwards
            case SessionProgress progress when State == SessionState.Finalising
                && Phase < FinalisePhase.Note:
                Phase = progress.Stage switch
                {
                    "transcript" => FinalisePhase.Transcript,
                    "speakers" => FinalisePhase.Speakers,
                    "turns" => FinalisePhase.Turns,
                    _ => Phase,
                };
                break;
            // Writing is claimed only once tokens stream; Review included because
            // a regenerate streams there
            case NotePartial chunk when State is SessionState.Finalising or SessionState.Review:
                if (Note.ClinicalNoteText.Length == 0)
                {
                    Status.Append("Writing clinical note", busy: true);
                    Phase = FinalisePhase.Streaming;  // the panes open on the first token
                }

                Note.ClinicalNoteText = chunk.Text;
                if (!_regenerating)
                {
                    _metrics?.NotePartial(chunk.TokensPerSecond);
                }

                break;
            case NoteReady ready when State is SessionState.Finalising or SessionState.Review:
                if (ready.Text is { } noteText)
                {
                    Note.ClinicalNoteText = noteText;
                }

                Note.Apply(NotePipelineEvent.NoteReady);
                _loadedNote = Note.ClinicalNoteText;
                State = SessionState.Review;
                Guidance.NoteReady();
                if (!_regenerating)
                {
                    _metrics?.NoteReady(ready.TokensPerSecond);
                }

                break;
            // Too short or not a consultation: nothing to review, so the record
            // region says why and offers the override (unless it was too short);
            // a refusal while already reviewing shows in the note pane instead
            case NoteRefused refused when State is SessionState.Finalising or SessionState.Review:
                Note.RefusalReason = refused.Reason;
                Note.WriteAnywayAvailable = refused.Overridable;
                Note.Apply(NotePipelineEvent.NoteRefused);
                Guidance.NoteFailed();
                State = State == SessionState.Finalising ? SessionState.Refused : SessionState.Review;
                Status.Append("No note - too short or not enough clinical information");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync("refused: " + Note.RefusalReason, 0);
                }

                break;
            // The transcript is still usable, so review proceeds without a note
            case NoteFailed failed when State is SessionState.Finalising or SessionState.Review:
                Note.Apply(NotePipelineEvent.NoteFailed);
                Guidance.NoteFailed();
                State = SessionState.Review;
                Status.Append("Clinical note failed");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(failed.Detail, 0);
                }

                _regenerating = false;
                break;
            case PatientPartial chunk:
                if (Note.PatientInfoText.Length == 0)
                {
                    Status.Append("Writing patient note", busy: true);
                }

                Note.PatientInfoText = chunk.Text;
                if (!_regenerating)
                {
                    _metrics?.PatientPartial(chunk.TokensPerSecond);
                }

                break;
            case PatientReady ready:
                if (ready.Text is { } patientText)
                {
                    Note.PatientInfoText = patientText;
                }

                Note.Apply(NotePipelineEvent.PatientInfoReady);
                _loadedPatient = Note.PatientInfoText;
                Note.PatientStale = false;  // freshly written from the note
                Status.Append("Ready for review");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(null, Note.ClinicalNoteText.Length,
                        patientTokensPerSecond: ready.TokensPerSecond);
                }

                _regenerating = false;
                break;
            case PatientFailed:
                Note.Apply(NotePipelineEvent.PatientInfoFailed);
                Status.Append("Patient note failed");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(null, Note.ClinicalNoteText.Length, "failed");
                }

                _regenerating = false;
                break;
            case TranslationPartial chunk:
                Note.TranslationText = chunk.Text;
                break;
            case TranslationReady ready:
                Note.TranslationText = ready.Text;
                Note.TranslationLanguage = ready.Language;
                Note.TranslationRunning = false;
                Status.Append($"Translated to {Note.TranslationLanguage}");
                break;
            case TranslationFailed:
                Note.TranslationRunning = false;
                Status.Append("Translation failed");
                break;
            case GuidanceModelChanged:
                _ = LoadGuidanceReadinessAsync();
                break;
            case GuidanceReady ready:
                ApplyGuidance(ready.Record);
                break;
            case GuidanceFailed failed:
                ApplyGuidanceFailed(failed);
                break;
            case GuidanceDocumentsChanged:
                Guidance.DocumentsChanged();
                SearchAfterDocumentsSettle();
                break;
            case AudioLevel level:
                Status.SetMicLevel(level.Level, level.Clipped);
                // A playback's readings carry the position its clock has reached
                if (State == SessionState.Recording)
                {
                    AudioSeconds = level.Seconds ?? AudioSeconds + 0.1;
                    // A playback stops itself at the end of its clock
                    if (ActivePlayback is { } playing && AudioSeconds >= playing.AudioSeconds - 0.05)
                    {
                        _ = StopRecordingAsync();
                    }
                }

                break;
            case SessionInterrupted interrupted
                when State is SessionState.Recording or SessionState.Finalising:
                State = SessionState.Idle;
                Paused = false;
                ActiveReplay = null;
                ActivePlayback = null;
                Note.Reset();
                Guidance.Reset();
                PageView.Hide();
                Status.SetMicVisible(false);
                Status.SetDecodeActive(false);
                Status.Append(interrupted.Detail is { } detail
                    ? $"Recording interrupted ({detail}) - session kept"
                    : "Recording interrupted - session kept");
                break;
            default:
                break;
        }
    }

    // A failed step is reported and the flow carries on: false, or null for a value
    private async Task<bool> TryAsync(string step, Func<Task> call) =>
        await TryAsync(step, async () =>
        {
            await call().ConfigureAwait(true);
            return true;
        }).ConfigureAwait(true);

    private async Task<T?> TryAsync<T>(string step, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status.Append("Taking longer than expected", busy: true);
            return default;
        }
        catch (Exception e)
        {
            Status.Append("A step failed - trying to continue");
            Status.Log($"{step} failed: {e.Message}");
            return default;
        }
    }
}
