using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Consultation;

/// <summary>
/// What the engine has to offer once connected: the first-use compile gate, the note
/// options it must be told again after every restart, the translation languages and the
/// guidance embedder's state.
/// </summary>
public sealed partial class EngineReadiness : ObservableObject
{
    private readonly IEngineApi _engine;
    private readonly StatusBarViewModel _status;
    private readonly NoteViewModel _note;
    private readonly GuidanceViewModel _guidance;
    private readonly SessionRecorder _recorder;
    private readonly AppPreferences? _preferences;
    private readonly TimeSpan _pollInterval;
    private bool _checking;

    public EngineReadiness(
        IEngineApi engine, StatusBarViewModel status, NoteViewModel note, GuidanceViewModel guidance,
        SessionRecorder recorder, AppPreferences? preferences, TimeSpan pollInterval)
    {
        _engine = engine;
        _status = status;
        _note = note;
        _guidance = guidance;
        _recorder = recorder;
        _preferences = preferences;
        _pollInterval = pollInterval;
    }

    /// <summary>
    /// False only during first-time setup, while the one-off model compiles
    /// run: recording is blocked so nobody's first impression is the slow
    /// path. Warm launches are never gated.
    /// </summary>
    [ObservableProperty]
    public partial bool ModelsReady { get; private set; } = true;

    /// <summary>The engine connected, or was there at start: everything it must be asked.</summary>
    public void Connected()
    {
        _ = LoadLanguagesAsync();
        _ = LoadGuidanceReadinessAsync();
        _ = ConfigureThenCheckReadinessAsync();
    }

    // Polled rather than only listened for: the embedder loads before the
    // shell connects, so its notification can be missed
    public async Task LoadGuidanceReadinessAsync()
    {
        var before = (_guidance.Readiness, _guidance.ReadinessDetail, _guidance.RefusedCorpora.Count);
        try
        {
            _guidance.ApplyCorpora(await _engine.GuidanceCorporaAsync().ConfigureAwait(true));
        }
        catch (Exception e)
        {
            _guidance.CorporaUnavailable();
            _status.Log($"guidance/corpora failed: {e.Message}");
        }

        // Logged on change only: the poll repeats on every model transition
        if ((_guidance.Readiness, _guidance.ReadinessDetail, _guidance.RefusedCorpora.Count) == before)
        {
            return;
        }

        if (_guidance.ReadinessDetail.Length > 0)
        {
            _status.Log($"guidance unavailable: {_guidance.ReadinessDetail}");
        }

        foreach (var refused in _guidance.RefusedCorpora)
        {
            _status.Log($"guidance corpus refused: {refused}");
        }
    }

    /// <summary>The note lane's model changed; a first-use compile gates recording while it runs.</summary>
    public void NoteModelChanged(NoteModelState model)
    {
        // A warm model load never blocks recording; only the first-use compile does
        if (_recorder.State != SessionState.Idle)
        {
            return;
        }

        if (model.State == "loading" && model.FirstUse)
        {
            ModelsReady = false;
            _status.Append("Preparing note model for this computer - this can take a few minutes",
                busy: true);
        }
        else if (model.State is "ready" or "failed" && !ModelsReady)
        {
            ModelsReady = true;
            _status.Append("Ready");
        }
    }

    public void NoteOptionsChanged()
    {
        if (_preferences is not null)
        {
            _preferences.NoteStyle = _note.Style;
            _preferences.NoteDetail = _note.Detail;
            _preferences.Save();
        }

        _ = PushNoteOptionsAsync();
    }

    // Tier before readiness: readiness reports the configured tier's compile cache
    private async Task ConfigureThenCheckReadinessAsync()
    {
        await PushNoteOptionsAsync().ConfigureAwait(true);
        await CheckReadinessAsync().ConfigureAwait(true);
    }

    // Engine options are per process: resent after a restart. The tier is a
    // role; the engine's store resolves it
    private async Task PushNoteOptionsAsync()
    {
        if (_engine.Connected)
        {
            await EngineStep.TryAsync(_status, "note/tier",
                () => _engine.SetNoteTierAsync(_preferences?.NoteTier ?? "default")).ConfigureAwait(true);
            await EngineStep.TryAsync(_status, "note/options",
                () => _engine.SetNoteOptionsAsync(_note.Style, _note.Detail)).ConfigureAwait(true);
        }
    }

    // First launch only: poll until the one-off compiles finish, then never
    // again. Fails open - a readiness error must not brick recording.
    private async Task CheckReadinessAsync()
    {
        // Every reconnect calls this; one poll loop at a time
        if (_checking)
        {
            return;
        }

        _checking = true;
        try
        {
            var readiness = await _engine.ReadinessAsync().ConfigureAwait(true);
            // A note host wedged in the GPU driver outlives the engine; only a reboot ends it
            if (readiness.StrayNoteHost)
            {
                _status.Append("A previous note process is stuck in the graphics driver - restart the computer");
                _status.Log("stray note host detected at engine start");
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
                    _status.Append("First-time setup - this can take a few minutes", busy: true);
                }

                await Task.Delay(_pollInterval).ConfigureAwait(true);
                readiness = await _engine.ReadinessAsync().ConfigureAwait(true);
            }

            if (!ModelsReady)
            {
                ModelsReady = true;
                _status.Append("Ready");
            }
        }
        catch (Exception)
        {
            ModelsReady = true;
        }
        finally
        {
            _checking = false;
        }
    }

    // Empty when the engine ships without a translation model
    private async Task LoadLanguagesAsync()
    {
        try
        {
            var languages = await _engine.LanguagesAsync().ConfigureAwait(true);
            _note.Languages.Clear();
            foreach (var language in languages)
            {
                _note.Languages.Add(language);
            }
        }
        catch (Exception)
        {
        }
    }
}
