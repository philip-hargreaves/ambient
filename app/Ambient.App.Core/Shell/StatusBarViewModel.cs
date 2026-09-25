using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Common;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Shell;

/// <summary>
/// The status line, its busy ring, the activity log and the testing chips: the models with
/// their live numbers and the product's memory. Clinician-facing text carries no engine,
/// model or process vocabulary.
/// </summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    private readonly IEngineApi? _engine;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly ThroughputMeter _meter = new();
    private readonly Func<double> _memoryGb = () => 0;
    private readonly long _started;

    private EngineStatus _status = EngineStatus.Stopped;
    private bool _ready;
    private bool _activityBusy;
    private bool _polling;
    private double _frozenRealtime;
    private string _asrName = "";
    private string _noteName = "";
    private string _asrDevice = "";
    private string _noteDevice = "";

    public StatusBarViewModel(ILogger? logger = null) => _logger = logger;

    /// <summary>
    /// With an engine, the bar meters generation live: one partial per token
    /// from whichever lane streams, so the number moves with every token.
    /// </summary>
    public StatusBarViewModel(IEngineApi engine, IUiDispatcher dispatcher,
        TimeProvider? time = null, Func<double>? memoryGb = null, ILogger? logger = null)
    {
        _engine = engine;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _memoryGb = memoryGb ?? (() => 0);
        _started = _time.GetTimestamp();
        engine.OnConnected(dispatcher, () =>
        {
            _ = LoadModelsAsync();
            StartPolling();
        });

        engine.NotificationReceived += notification => dispatcher.Post(() =>
        {
            switch (notification)
            {
                case NotePartial or PatientPartial or TranslationPartial:
                    _meter.Token(Now());
                    PublishThroughput(SourceRate(notification));
                    break;
                case NoteReady or PatientReady or TranslationReady:
                    _meter.End(Now());
                    // The ready event carries the whole-generation average
                    PublishThroughput(SourceRate(notification));
                    break;
                case NoteFailed or PatientFailed or TranslationFailed:
                    _meter.End(Now());
                    PublishThroughput(null);
                    break;
                // A tier switch changes which model the chip names. The notification
                // carries the name, so the chip is right even when the store call
                // behind it times out on a busy engine
                case NoteModelState { State: "ready" } resident:
                    if (!string.IsNullOrWhiteSpace(resident.Name))
                    {
                        _noteName = resident.Name;
                        RecomputeChips();
                    }

                    _ = LoadModelsAsync();
                    break;
                default:
                    break;
            }
        });
    }

    // ---- engine state and the status line

    [ObservableProperty]
    public partial string EngineStateLabel { get; private set; } = "Starting";

    /// <summary>True in every transient state. The status ring spins on it.</summary>
    [ObservableProperty]
    public partial bool EngineStarting { get; private set; }

    [ObservableProperty]
    public partial string LatestActivity { get; private set; } = "";

    /// <summary>Demo mode is on, or a demo record is on screen. Shown beside the app name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemoLabel))]
    public partial bool Demo { get; set; }

    // ---- microphone

    [ObservableProperty]
    public partial double MicLevel { get; private set; }

    /// <summary>The level bar shows only while audio is flowing.</summary>
    [ObservableProperty]
    public partial bool MicVisible { get; private set; }

    // ---- throughput and transcription speed

    /// <summary>Rolling tokens per second. Holds its last value after a stream ends.</summary>
    [ObservableProperty]
    public partial double TokensPerSecond { get; private set; }

    [ObservableProperty]
    public partial bool TokensStreaming { get; private set; }

    /// <summary>Transcription speed as a multiple of real time, 0 when unknown.</summary>
    [ObservableProperty]
    public partial double RealtimeFactor { get; private set; }

    /// <summary>
    /// True from stop until the sealed transcript loads: the finalise tail
    /// decode, the NPU's longest stage, keeps the RT figure on screen.
    /// </summary>
    [ObservableProperty]
    public partial bool DecodeActive { get; private set; }

    // ---- the testing chips

    /// <summary>The chips are for testing: off unless opted in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsrChipVisible), nameof(NoteChipVisible), nameof(MemoryChipVisible))]
    public partial bool MetricsVisible { get; set; }

    // The two models a clinician's machine actually works for, each with its
    // live number: "Whisper Turbo · GPU · 33× RT", "Qwen3.5 9B · GPU · 14.2 tok/s"
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsrChipVisible))]
    public partial string AsrChip { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteChipVisible))]
    public partial string NoteChip { get; private set; } = "";

    /// <summary>"Memory · 5.1 GB": the product's whole working set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryChipVisible))]
    public partial string MemoryChip { get; private set; } = "";

    // One status on screen, replaced as things happen: abnormal readiness
    // outranks activity, activity outranks Ready
    public string DisplayLabel =>
        !_ready || _status != EngineStatus.Running ? EngineStateLabel
        : LatestActivity.Length > 0 ? LatestActivity
        : EngineStateLabel;

    public bool Busy => EngineStarting || _activityBusy;

    public string DemoLabel => Demo ? "Demo" : "";

    /// <summary>Amber: transcription is barely keeping up with the room.</summary>
    public bool RealtimeLow =>
        (MicVisible || DecodeActive) && RealtimeFactor > 0 && RealtimeFactor < 2;

    public bool AsrChipVisible => MetricsVisible && AsrChip.Length > 0;

    public bool NoteChipVisible => MetricsVisible && NoteChip.Length > 0;

    public bool MemoryChipVisible => MetricsVisible && MemoryChip.Length > 0;

    /// <summary>The chip's dot: green while this model is working right now.</summary>
    public bool AsrActive => MicVisible || DecodeActive;

    public bool NoteActive => TokensStreaming;

    // The resting dot and healthy text are the visible-inverse halves of the
    // colour pairs the view swaps, exposed as properties for XAML binding
    public bool AsrResting => !AsrActive;

    public bool NoteResting => !NoteActive;

    public bool RealtimeHealthy => !RealtimeLow;

    public void SetEngineState(EngineStatus status)
    {
        _status = status;
        Recompute();

        // Silent restarts stay out of the activity log. Faults go in
        if (status == EngineStatus.Faulted)
        {
            Append(EngineStateLabel);
        }
    }

    public void SetEngineReady(bool ready)
    {
        _ready = ready;
        Recompute();
    }

    /// <summary>Log-only detail, to the file. The displayed status stays concise.</summary>
    public void Log(string line) => _logger?.Line(line);

    /// <summary>The status line, also kept in the log.</summary>
    public void Append(string line, bool busy = false)
    {
        _logger?.Line(line);
        _activityBusy = busy;
        LatestActivity = line;
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(Busy));
    }

    public void SetMicLevel(double level) => MicLevel = level;

    public void SetMicVisible(bool visible)
    {
        MicVisible = visible;
        OnPropertyChanged(nameof(RealtimeLow));
        RecomputeChips();
        if (!visible)
        {
            SetMicLevel(0);
        }
    }

    /// <summary>A new consultation meters from nothing.</summary>
    public void ResetThroughput()
    {
        _meter.Reset();
        _frozenRealtime = 0;
        PublishThroughput();
    }

    public void SetDecodeActive(bool active)
    {
        if (DecodeActive && !active && RealtimeFactor > 0)
        {
            // Sealed: the per-session counters make this the session's
            // exact average decode speed, held for reading after the run
            _frozenRealtime = RealtimeFactor;
        }

        DecodeActive = active;
        OnPropertyChanged(nameof(RealtimeLow));
        RecomputeChips();
    }

    // Shell, engine and note host: the whole on-device footprint, found by
    // name because the note host is the engine's child process. Failures
    // leave the last value.
    public async Task PollMetricsOnceAsync()
    {
        var memory = await Task.Run(_memoryGb).ConfigureAwait(true);
        MemoryChip = memory > 0
            ? $"Memory · {memory.ToString("0.0", CultureInfo.CurrentCulture)} GB"
            : "";

        if (!_engine.IsConnected())
        {
            return;
        }

        await EngineCall.LogAsync(this, "engine/metrics", async () =>
        {
            var metrics = await _engine.MetricsAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            if (metrics.AsrRealtimeFactor is { } factor)
            {
                RealtimeFactor = factor;
            }

            if (metrics.Devices is { } devices)
            {
                _asrDevice = ShortDevice(devices.Asr ?? _asrDevice);
                _noteDevice = ShortDevice(devices.Note ?? _noteDevice);
                RecomputeChips();
            }
        }).ConfigureAwait(true);
    }

    partial void OnRealtimeFactorChanged(double value)
    {
        OnPropertyChanged(nameof(RealtimeLow));
        RecomputeChips();
    }

    private void Recompute()
    {
        EngineStarting = _status == EngineStatus.Running && !_ready
            || _status == EngineStatus.Restarting;
        EngineStateLabel = _status switch
        {
            EngineStatus.Running when !_ready => "Starting up",
            EngineStatus.Running => "Ready",
            EngineStatus.Restarting => "Recovering",
            EngineStatus.Faulted => "Recording is unavailable - please restart the app",
            _ => "Not running",
        };
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(Busy));
    }

    private double Now() => _time.GetElapsedTime(_started).TotalSeconds;

    private void PublishThroughput(double? sourceRate = null)
    {
        TokensPerSecond = sourceRate ?? _meter.TokensPerSecond(Now());
        TokensStreaming = _meter.Streaming;
        RecomputeChips();
    }

    private async Task LoadModelsAsync()
    {
        if (!_engine.IsConnected())
        {
            return;
        }

        await EngineCall.LogAsync(this, "engine/models", async () =>
        {
            var models = await _engine.ListModelsAsync().ConfigureAwait(true);
            // The chip names the model the engine marks active for the role.
            // Older engines send no flag, so the default tier is assumed
            _asrName = _noteName = "";
            foreach (var model in models.OrderBy(m => m.Active ? 0 : m.Tier == "default" ? 1 : 2))
            {
                var name = string.IsNullOrWhiteSpace(model.Name)
                    ? ModelNames.Friendly(model.Id)
                    : model.Name;
                var device = ShortDevice(model.Device);
                if (model.Task == "asr" && _asrName.Length == 0)
                {
                    (_asrName, _asrDevice) = (name, device);
                }
                else if (model.Task == "note" && _noteName.Length == 0)
                {
                    (_noteName, _noteDevice) = (name, device);
                }
            }
        }).ConfigureAwait(true);

        RecomputeChips();
        await PollMetricsOnceAsync().ConfigureAwait(true);  // actual devices beat manifests
    }

    // Live figures are unlabelled and move. Settled ones say "Averaged": the
    // session's true average, held through review for reading after a run
    private void RecomputeChips()
    {
        OnPropertyChanged(nameof(AsrActive));
        OnPropertyChanged(nameof(NoteActive));
        OnPropertyChanged(nameof(AsrResting));
        OnPropertyChanged(nameof(NoteResting));
        OnPropertyChanged(nameof(RealtimeHealthy));
        AsrChip = Chip(_asrName, _asrDevice,
            (MicVisible || DecodeActive) && RealtimeFactor > 0
                ? $"{Figure(RealtimeFactor)}× RT"
                : _frozenRealtime > 0 ? $"Averaged {Figure(_frozenRealtime)}× RT" : "");
        var tok = TokensPerSecond.ToString("0.0", CultureInfo.CurrentCulture);
        NoteChip = Chip(_noteName, _noteDevice,
            TokensPerSecond <= 0 ? ""
            : TokensStreaming ? $"{tok} tok/s"
            : $"Averaged {tok} tok/s");

        static string Figure(double value) => value.ToString(
            value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);

        static string Chip(string name, string device, string figure)
        {
            if (name.Length == 0)
            {
                return "";
            }

            var chip = device.Length > 0 ? $"{name} · {device}" : name;
            return figure.Length > 0 ? $"{chip} · {figure}" : chip;
        }
    }

    private void StartPolling()
    {
        if (_engine is not null && !_polling)
        {
            _ = PollWhileConnectedAsync();
        }
    }

    // One loop for the connected lifetime: 1 Hz while recording, 5 s idle,
    // so a device switch reaches the chip within seconds
    private async Task PollWhileConnectedAsync()
    {
        _polling = true;
        try
        {
            while (_engine!.Connected)
            {
                await PollMetricsOnceAsync().ConfigureAwait(true);
                await Task.Delay(TimeSpan.FromSeconds(MicVisible || DecodeActive ? 1 : 5), _time)
                    .ConfigureAwait(true);
            }
        }
        finally
        {
            _polling = false;  // reconnection starts a fresh loop
        }
    }

    // The engine measures at the source, before its notification throttle,
    // so its figure beats the local arrival count whenever it is present
    private static double? SourceRate(EngineNotification notification) =>
        (notification as IMetered)?.TokensPerSecond;

    private static string ShortDevice(string device) =>
        device.Split('.')[0];  // the device index is a build detail
}
