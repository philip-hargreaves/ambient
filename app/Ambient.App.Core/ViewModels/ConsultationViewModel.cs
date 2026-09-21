using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.App.Core.Demo;
using Ambient.App.Core.Hosting;
using Ambient.Client;

namespace Ambient.App.Core.ViewModels;

/// <summary>A file replayed as the session's audio source.</summary>
public sealed record ReplayRequest(string Path, double Speed, bool Monitor);

public sealed partial class ConsultationViewModel : ObservableObject, ISessionState
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    // Accelerated replay legitimately leaves a decode backlog for stop to
    // drain; at 1x this is seconds
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(180);

    private readonly IEngineClient _engine;
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
    /// True once the sealed transcript has been fetched; the panes open on
    /// this, never on state alone - a pane with no transcript is worse than
    /// the centred spinner it would replace.
    /// </summary>
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
    public PageViewModel PageView { get; } = new();

    public StatusBarViewModel Status { get; }

    public ConsultationViewModel(
        IEngineClient engine, IUiDispatcher dispatcher,
        TranscriptViewModel transcript, NoteViewModel note, StatusBarViewModel status,
        Metrics.PerformanceCollector? metrics = null, TimeSpan? readinessPollInterval = null,
        AppPreferences? preferences = null, GuidanceViewModel? guidance = null,
        DemoMode? demo = null)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _metrics = metrics;
        _preferences = preferences;
        _demo = demo;
        _readinessPollInterval = readinessPollInterval ?? TimeSpan.FromSeconds(2);
        Transcript = transcript;
        Note = note;
        Guidance = guidance ?? new GuidanceViewModel();
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
        PageView.Request = (method, parameters) =>
            _engine.RequestAsync(method, parameters, RequestTimeout);
        PageView.Report = line => Status.Append(line);
        // Persisted options applied before the change callback is wired,
        // so restoring them is not itself a change
        if (preferences is not null)
        {
            Note.Style = preferences.NoteStyle;
            Note.Detail = preferences.NoteDetail;
        }

        Note.OptionsChanged = OnNoteOptionsChanged;
        _engine.NotificationReceived +=
            (method, parameters) => dispatcher.Post(() => HandleNotification(method, parameters));
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
            var corpora = await _engine
                .RequestAsync("guidance/corpora", null, RequestTimeout)
                .ConfigureAwait(true);
            Guidance.ApplyCorpora(corpora);
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

        if (!await RequestAsync("guidance/search", new { id }).ConfigureAwait(true))
        {
            Guidance.ApplyFailed();
        }
    }

    private async Task SearchGuidanceAsync(string text)
    {
        if (!await RequestAsync("guidance/search", new { text, limit = 3 }).ConfigureAwait(true))
        {
            Guidance.ApplyQueryFailed();
        }
    }

    // Results are keyed to the consultation on screen; a typed query has no id.
    // A search replaced by a newer one says so and changes nothing
    private void ApplyGuidance(JsonElement parameters, bool ready)
    {
        var detail = Text(parameters, "detail");
        if (detail == "superseded")
        {
            return;
        }

        var id = parameters.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()
            : null;
        if (id is null)
        {
            if (ready)
            {
                Guidance.ApplyQueryReady(parameters);
            }
            else
            {
                Guidance.ApplyQueryFailed();
                Status.Log($"guidance search failed: {detail}");
            }

            return;
        }

        if (id != _finalisedSessionId)
        {
            Status.Log($"guidance for another session dropped: {id}");
            return;
        }

        if (ready)
        {
            Guidance.ApplyReady(parameters);
            var storeError = Text(parameters, "storeError");
            if (storeError.Length > 0)
            {
                Status.Log($"guidance not stored: {storeError}");
            }
        }
        else
        {
            Guidance.ApplyFailed();
            Status.Log($"guidance search failed: {detail}");
        }
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
            var response = await _engine
                .RequestAsync("engine/readiness", null, RequestTimeout)
                .ConfigureAwait(true);
            // A note host wedged in the GPU driver outlives the engine; only a reboot ends it
            if (response.TryGetProperty("strayNoteHost", out var stray) && stray.GetBoolean())
            {
                Status.Append("A previous note process is stuck in the graphics driver - restart the computer");
                Status.Log("stray note host detected at engine start");
            }

            if (!response.TryGetProperty("firstUse", out var f) || !f.GetBoolean())
            {
                ModelsReady = true;
                return;
            }

            while (!response.GetProperty("ready").GetBoolean())
            {
                if (ModelsReady)
                {
                    ModelsReady = false;
                    Status.Append("First-time setup - this can take a few minutes", busy: true);
                }

                await Task.Delay(_readinessPollInterval).ConfigureAwait(true);
                response = await _engine
                    .RequestAsync("engine/readiness", null, RequestTimeout)
                    .ConfigureAwait(true);
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
            var response = await _engine
                .RequestAsync("translate/languages", null, RequestTimeout)
                .ConfigureAwait(true);
            Note.Languages.Clear();
            foreach (var language in response.GetProperty("languages").EnumerateArray())
            {
                Note.Languages.Add(language.GetString() ?? "");
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

        Note.TranslationText = "";
        await RequestAsync("patient/translate", new { id = _finalisedSessionId, language })
            .ConfigureAwait(true);
    }

    private string? _finalisedSessionId;
    private string _finalisedStartedAt = "";
    private readonly DemoMode? _demo;

    /// <summary>The view opens the reflection sheet for (session id, started at).</summary>
    public Func<string, string, Task>? OpenReflection { get; set; }

    // Opening the sheet creates the entry, so the button reads Open from here on
    private async Task ReflectAsync()
    {
        if (_finalisedSessionId is null || OpenReflection is null)
        {
            return;
        }

        await OpenReflection(_finalisedSessionId, _finalisedStartedAt).ConfigureAwait(true);
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
            await RequestAsync("note/tier", new { tier = _preferences?.NoteTier ?? "default" })
                .ConfigureAwait(true);
            await RequestAsync("note/options", new { style = Note.Style, detail = Note.Detail })
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
        var accepted = await RequestAsync("patient/regenerate", null).ConfigureAwait(true);
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

        var accepted = await RequestAsync(
            "note/regenerate", new { style = Note.Style, detail = Note.Detail })
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

        var accepted = await RequestAsync(
            "note/regenerate", new { style = Note.Style, detail = Note.Detail, confirmed = true })
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

        var saved = await RequestAsync("note/update", new { id, text }).ConfigureAwait(true);
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
        if (_finalisedSessionId is null)
        {
            return;
        }

        var saved = await RequestAsync(
            "patient/update", new { id = _finalisedSessionId, text = Note.PatientInfoText })
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
        if (!await RequestAsync("session/open", new { id }).ConfigureAwait(true))
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

        var note = await RequestValueAsync("session/note", null, new { id }).ConfigureAwait(true);
        var patient = await RequestValueAsync("session/patient", null, new { id })
            .ConfigureAwait(true);
        var (translation, translationLanguage) =
            patient is { ValueKind: JsonValueKind.Object } p
            && p.TryGetProperty("translation", out var t) && t.ValueKind == JsonValueKind.Object
                ? (Text(t, "text"), Text(t, "language"))
                : ("", "");
        Note.LoadStored(
            Text(note, "text"), Text(patient, "text"), translation,
            Text(note, "style"), Text(note, "detail"),
            EditedStamp.Label(Text(note, "generatedAt"), Text(note, "editedAt")),
            translationLanguage);
        _loadedNote = Note.ClinicalNoteText;
        _loadedPatient = Note.PatientInfoText;
        // ISO 8601 UTC compares as text: the sheet predates the note edit
        var noteEdited = Text(note, "editedAt");
        var sheetWritten = Text(patient, "generatedAt");
        Note.PatientStale = noteEdited.Length > 0 && sheetWritten.Length > 0
            && string.CompareOrdinal(noteEdited, sheetWritten) > 0;
        // What this note was shown, without a model; nothing to read for an empty note
        if (Note.ClinicalNoteText.Length > 0)
        {
            var guidance = await RequestValueAsync("session/guidance", null, new { id })
                .ConfigureAwait(true);
            var stored = guidance is { ValueKind: JsonValueKind.Object } g
                && g.TryGetProperty("guidance", out var record)
                    ? record
                    : (JsonElement?)null;
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

    private static string Text(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } o
            && o.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>Leaves the review or a refusal: edits saved, the engine told
    /// (which deletes a refused session), panes cleared.</summary>
    public async Task CloseReviewAsync()
    {
        if (State is not (SessionState.Review or SessionState.Refused))
        {
            return;
        }

        await AutosaveReviewAsync().ConfigureAwait(true);
        await RequestAsync("session/close").ConfigureAwait(true);
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
            var replay = ActiveReplay;
            var retain = _preferences?.KeepConsultations ?? true;
            var parameters = replay is null
                ? (object)new { resume, retain }
                : new
                {
                    resume,
                    retain,
                    replay = new { path = replay.Path, speed = replay.Speed, monitor = replay.Monitor },
                };
            // A post-crash resume decrypts stored audio, far beyond the default
            // timeout; the raw request tells a lost engine from one that answered
            var response = await _engine.RequestAsync(
                "session/start", parameters, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            _recordingSessionId = response.TryGetProperty("sessionId", out var id)
                ? id.GetString() : null;
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

        // Keep consultations off: the engine erases the session once it is left
        var retain = _preferences?.KeepConsultations ?? true;
        var parameters = replay is null
            // The engine pins this microphone; empty means the default, and
            // an id that has gone falls back to the default there, logged
            ? (object)new { retain, micId = _preferences?.MicId ?? "" }
            : new
            {
                retain,
                replay = new { path = replay.Path, speed = replay.Speed, monitor = replay.Monitor },
            };
        if (!await BeginAsync(parameters, replay, null).ConfigureAwait(true))
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

        var parameters = new { playback = new { id = playback.SessionId } };
        if (!await BeginAsync(parameters, null, playback).ConfigureAwait(true))
        {
            return;
        }

        ShowDemo(true);
        Status.Append("Recording");
    }

    private async Task<bool> BeginAsync(
        object parameters, ReplayRequest? replay, DemoMaster? playback)
    {
        // Beyond the engine's 10 s no-audio deadline: a Bluetooth link wakes in
        // seconds and a timeout here would abandon a started session
        var response = await RequestValueAsync(
            "session/start", TimeSpan.FromSeconds(30), parameters).ConfigureAwait(true);
        if (response is null)
        {
            return false;
        }

        _recordingSessionId = response.Value.TryGetProperty("sessionId", out var id)
            ? id.GetString() : null;
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

        if (await RequestAsync("session/pause", new { paused }).ConfigureAwait(true))
        {
            Paused = paused;
        }
    }

    public async Task SetMonitorAsync(bool on)
    {
        if (State == SessionState.Recording)
        {
            await RequestAsync("session/monitor", new { on }).ConfigureAwait(true);
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
        var response = await RequestValueAsync("session/stop", StopTimeout).ConfigureAwait(true);
        if (response is null)
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
        if (response is { ValueKind: JsonValueKind.Object } stop
            && stop.TryGetProperty("sessionId", out var sessionId))
        {
            _finalisedSessionId = sessionId.GetString();
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
            var response = await _engine
                .RequestAsync("session/transcript", new { id }, RequestTimeout)
                .ConfigureAwait(true);
            Transcript.Turns.Clear();
            foreach (var turn in response.GetProperty("turns").EnumerateArray())
            {
                Transcript.Add(
                    turn.GetProperty("speaker").GetString() ?? "",
                    turn.GetProperty("firstFrame").GetUInt64(),
                    turn.GetProperty("text").GetString() ?? "");
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

        if (!await RequestAsync("session/cancel").ConfigureAwait(true))
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

    // The engine's own token rate, when the notification carries one
    private static double? Rate(JsonElement parameters) =>
        parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("tokensPerSecond", out var rate)
            && rate.ValueKind == JsonValueKind.Number
        ? rate.GetDouble()
        : null;

    private void HandleNotification(string method, JsonElement parameters)
    {
        if (method == "note/model" && parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("state", out var lane) && lane.GetString() == "ready")
        {
            _metrics?.NoteModel(
                parameters.TryGetProperty("name", out var name) ? name.GetString() : null,
                parameters.TryGetProperty("tier", out var tier) ? tier.GetString() : null,
                parameters.TryGetProperty("seconds", out var sec) && sec.ValueKind == JsonValueKind.Number
                    ? sec.GetDouble() : null);
        }

        switch (method)
        {
            // A warm model load never blocks recording; only the first-use compile does
            case "note/model" when State == SessionState.Idle
                && parameters.ValueKind == JsonValueKind.Object:
                var laneState = parameters.TryGetProperty("state", out var s) ? s.GetString() : "";
                var firstUse = parameters.TryGetProperty("firstUse", out var f) && f.GetBoolean();
                if (laneState == "loading" && firstUse)
                {
                    ModelsReady = false;
                    Status.Append("Preparing note model for this computer - this can take a few minutes",
                        busy: true);
                }
                else if ((laneState is "ready" or "failed") && !ModelsReady)
                {
                    ModelsReady = true;
                    Status.Append("Ready");
                }

                break;
            // Stages the engine skips never show; a late stage cannot move the
            // phase backwards
            case "session/progress" when State == SessionState.Finalising
                && Phase < FinalisePhase.Note
                && parameters.ValueKind == JsonValueKind.Object:
                Phase = parameters.GetProperty("stage").GetString() switch
                {
                    "transcript" => FinalisePhase.Transcript,
                    "speakers" => FinalisePhase.Speakers,
                    "turns" => FinalisePhase.Turns,
                    _ => Phase,
                };
                break;
            // Writing is claimed only once tokens stream; Review included because
            // a regenerate streams there
            case "note/partial" when State is SessionState.Finalising or SessionState.Review
                && parameters.ValueKind == JsonValueKind.Object:
                if (Note.ClinicalNoteText.Length == 0)
                {
                    Status.Append("Writing clinical note", busy: true);
                    Phase = FinalisePhase.Streaming;  // the panes open on the first token
                }

                Note.ClinicalNoteText = parameters.GetProperty("text").GetString() ?? "";
                if (!_regenerating)
                {
                    _metrics?.NotePartial(Rate(parameters));
                }

                break;
            case "note/ready" when State is SessionState.Finalising or SessionState.Review:
                if (parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("text", out var noteText))
                {
                    Note.ClinicalNoteText = noteText.GetString() ?? "";
                }

                Note.Apply(NotePipelineEvent.NoteReady);
                _loadedNote = Note.ClinicalNoteText;
                State = SessionState.Review;
                Guidance.NoteReady();
                if (!_regenerating)
                {
                    _metrics?.NoteReady(Rate(parameters));
                }

                break;
            // Too short or not a consultation: nothing to review, so the record
            // region says why and offers the override (unless it was too short);
            // a refusal while already reviewing shows in the note pane instead
            case "note/refused" when State is SessionState.Finalising or SessionState.Review:
                Note.RefusalReason = parameters.ValueKind == JsonValueKind.Object
                    ? parameters.GetProperty("reason").GetString() ?? ""
                    : "";
                Note.WriteAnywayAvailable = parameters.ValueKind != JsonValueKind.Object
                    || !parameters.TryGetProperty("overridable", out var overridable)
                    || overridable.GetBoolean();
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
            case "note/failed" when State is SessionState.Finalising or SessionState.Review:
                Note.Apply(NotePipelineEvent.NoteFailed);
                Guidance.NoteFailed();
                State = SessionState.Review;
                var noteFailure = parameters.ValueKind == JsonValueKind.Object
                    ? parameters.GetProperty("detail").GetString() ?? "failed"
                    : "failed";
                Status.Append("Clinical note failed");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(noteFailure, 0);
                }

                _regenerating = false;
                break;
            case "patient/partial" when parameters.ValueKind == JsonValueKind.Object:
                if (Note.PatientInfoText.Length == 0)
                {
                    Status.Append("Writing patient note", busy: true);
                }

                Note.PatientInfoText = parameters.GetProperty("text").GetString() ?? "";
                if (!_regenerating)
                {
                    _metrics?.PatientPartial(Rate(parameters));
                }

                break;
            case "patient/ready":
                if (parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("text", out var patientText))
                {
                    Note.PatientInfoText = patientText.GetString() ?? "";
                }

                Note.Apply(NotePipelineEvent.PatientInfoReady);
                _loadedPatient = Note.PatientInfoText;
                Note.PatientStale = false;  // freshly written from the note
                Status.Append("Ready for review");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(null, Note.ClinicalNoteText.Length,
                        patientTokensPerSecond: Rate(parameters));
                }

                _regenerating = false;
                break;
            case "patient/failed":
                Note.Apply(NotePipelineEvent.PatientInfoFailed);
                Status.Append("Patient note failed");
                if (_metrics is not null && !_regenerating)
                {
                    _ = _metrics.SessionFinishedAsync(null, Note.ClinicalNoteText.Length, "failed");
                }

                _regenerating = false;
                break;
            case "translate/partial" when parameters.ValueKind == JsonValueKind.Object:
                Note.TranslationText = parameters.GetProperty("text").GetString() ?? "";
                break;
            case "translate/ready" when parameters.ValueKind == JsonValueKind.Object:
                Note.TranslationText = parameters.GetProperty("text").GetString() ?? "";
                Note.TranslationLanguage = parameters.GetProperty("language").GetString() ?? "";
                Note.TranslationRunning = false;
                Status.Append($"Translated to {Note.TranslationLanguage}");
                break;
            case "translate/failed":
                Note.TranslationRunning = false;
                Status.Append("Translation failed");
                break;
            case "guidance/model":
                _ = LoadGuidanceReadinessAsync();
                break;
            case "guidance/ready" when parameters.ValueKind == JsonValueKind.Object:
                ApplyGuidance(parameters, ready: true);
                break;
            case "guidance/failed" when parameters.ValueKind == JsonValueKind.Object:
                ApplyGuidance(parameters, ready: false);
                break;
            case "guidance/documentsChanged":
                Guidance.DocumentsChanged();
                SearchAfterDocumentsSettle();
                break;
            case "audio.level" when parameters.ValueKind == JsonValueKind.Object:
                Status.SetMicLevel(
                    parameters.GetProperty("level").GetDouble(),
                    parameters.GetProperty("clipped").GetBoolean());
                // A playback's readings carry the position its clock has reached
                if (State == SessionState.Recording)
                {
                    AudioSeconds = parameters.TryGetProperty("seconds", out var at)
                        ? at.GetDouble()
                        : AudioSeconds + 0.1;
                    // A playback stops itself at the end of its clock
                    if (ActivePlayback is { } playing && AudioSeconds >= playing.AudioSeconds - 0.05)
                    {
                        _ = StopRecordingAsync();
                    }
                }

                break;
            case "session/interrupted"
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
                Status.Append(parameters.ValueKind == JsonValueKind.Object
                    ? $"Recording interrupted ({parameters.GetProperty("detail").GetString()}) - session kept"
                    : "Recording interrupted - session kept");
                break;
            default:
                break;
        }
    }

    private async Task<bool> RequestAsync(
        string method, object? parameters = null, TimeSpan? timeout = null) =>
        await RequestValueAsync(method, timeout, parameters).ConfigureAwait(true) is not null;

    private async Task<JsonElement?> RequestValueAsync(
        string method, TimeSpan? timeout = null, object? parameters = null)
    {
        try
        {
            return await _engine.RequestAsync(method, parameters, timeout ?? RequestTimeout)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status.Append("Taking longer than expected", busy: true);
            return null;
        }
        catch (Exception e)
        {
            Status.Append("A step failed - trying to continue");
            Status.Log($"{method} failed: {e.Message}");
            return null;
        }
    }
}
