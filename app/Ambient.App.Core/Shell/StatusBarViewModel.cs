using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Shell;

public sealed partial class StatusBarViewModel : ObservableObject
{
    private const int MaxLogEntries = 200;

    // Clinician-facing: no engine, model or process vocabulary
    [ObservableProperty]
    public partial string EngineStateLabel { get; set; } = "Starting";

    [ObservableProperty]
    public partial string LatestActivity { get; private set; } = "";

    // One status on screen, replaced as things happen: abnormal readiness
    // outranks activity, activity outranks Ready. Busy drives the one ring
    public string DisplayLabel =>
        !_ready || _status != EngineStatus.Running ? EngineStateLabel
        : LatestActivity.Length > 0 ? LatestActivity
        : EngineStateLabel;

    public bool Busy => EngineStarting || _activityBusy;

    private bool _activityBusy;

    // The two models a clinician's machine actually works for, each with its
    // live number: "Whisper Turbo · GPU · 33× RT", "Qwen3.5 9B · GPU · 14.2 tok/s"
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsrChipVisible))]
    public partial string AsrChip { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteChipVisible))]
    public partial string NoteChip { get; private set; } = "";

    public bool AsrChipVisible => MetricsVisible && AsrChip.Length > 0;

    public bool NoteChipVisible => MetricsVisible && NoteChip.Length > 0;

    public bool MemoryChipVisible => MetricsVisible && MemoryChip.Length > 0;

    private string _asrName = "";
    private string _noteName = "";
    private string _asrDevice = "";
    private string _noteDevice = "";

    /// <summary>Fallback when a manifest has no display name: "whisper-turbo-int8"
    /// reads as "Whisper Turbo". The precision suffix is dropped, size tokens kept.</summary>
    public static string FriendlyModelName(string id)
    {
        var words = id.Split('-')
            .Where(t => t is not ("int8" or "int4" or "fp16" or "fp32"))
            .Select(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+b$")
                ? t.ToUpperInvariant()
                : char.ToUpperInvariant(t[0]) + t[1..]);
        return string.Join(' ', words);
    }

    private static string ShortDevice(string device) =>
        device.Split('.')[0];  // the device index is a build detail

    private async Task LoadModelsAsync()
    {
        if (_engine is null || !_engine.Connected)
        {
            return;
        }

        try
        {
            var models = await _engine.ListModelsAsync().ConfigureAwait(true);
            // The chip names the model the engine marks active for the role.
            // Older engines send no flag, so the default tier is assumed
            _asrName = _noteName = "";
            foreach (var model in models.OrderBy(m => m.Active ? 0 : m.Tier == "default" ? 1 : 2))
            {
                var name = string.IsNullOrWhiteSpace(model.Name)
                    ? FriendlyModelName(model.Id)
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
        }
        catch (Exception e)
        {
            Log($"engine/models failed: {e.Message}");
        }

        RecomputeChips();
        await PollMetricsOnceAsync().ConfigureAwait(true);  // actual devices beat manifests
    }

    /// <summary>The chip's dot: green while this model is working right now.</summary>
    public bool AsrActive => MicVisible || DecodeActive;

    public bool NoteActive => TokensStreaming;

    // The resting dot and healthy text are the visible-inverse halves of the
    // colour pairs the view swaps, exposed as properties for XAML binding
    public bool AsrResting => !AsrActive;

    public bool NoteResting => !NoteActive;

    public bool RealtimeHealthy => !RealtimeLow;

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
        var tok = TokensPerSecond.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        NoteChip = Chip(_noteName, _noteDevice,
            TokensPerSecond <= 0 ? ""
            : TokensStreaming ? $"{tok} tok/s"
            : $"Averaged {tok} tok/s");

        static string Figure(double value) => value.ToString(
            value < 10 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture);

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

    private readonly IEngineApi? _engine;
    private readonly ILogger? _logger;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly ThroughputMeter _meter = new();
    private readonly Func<double> _memoryGb = () => 0;
    private readonly long _started;

    public StatusBarViewModel()
    {
    }

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
        engine.ConnectedChanged += connected => dispatcher.Post(() =>
        {
            if (connected)
            {
                _ = LoadModelsAsync();
                StartPolling();
            }
        });
        if (engine.Connected)
        {
            _ = LoadModelsAsync();
            StartPolling();
        }

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
                // A tier switch changes which model the chip names
                case NoteModelState { State: "ready" }:
                    _ = LoadModelsAsync();
                    break;
                default:
                    break;
            }
        });
    }

    private double Now() => _time.GetElapsedTime(_started).TotalSeconds;

    // The engine measures at the source, before its notification throttle,
    // so its figure beats the local arrival count whenever it is present
    private static double? SourceRate(EngineNotification notification) =>
        (notification as IMetered)?.TokensPerSecond;

    private void PublishThroughput(double? sourceRate = null)
    {
        TokensPerSecond = sourceRate ?? _meter.TokensPerSecond(Now());
        TokensStreaming = _meter.Streaming;
        RecomputeChips();
    }

    /// <summary>Rolling tokens per second. Holds its last value after a stream ends.</summary>
    [ObservableProperty]
    public partial double TokensPerSecond { get; private set; }

    [ObservableProperty]
    public partial bool TokensStreaming { get; private set; }

    /// <summary>A new consultation meters from nothing.</summary>
    public void ResetThroughput()
    {
        _meter.Reset();
        _frozenRealtime = 0;
        PublishThroughput();
    }

    /// <summary>Transcription speed as a multiple of real time, 0 when unknown.</summary>
    [ObservableProperty]
    public partial double RealtimeFactor { get; private set; }

    /// <summary>
    /// True from stop until the sealed transcript loads: the finalise tail
    /// decode, the NPU's longest stage, keeps the RT figure on screen.
    /// </summary>
    [ObservableProperty]
    public partial bool DecodeActive { get; private set; }

    private double _frozenRealtime;

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

    /// <summary>Amber: transcription is barely keeping up with the room.</summary>
    public bool RealtimeLow =>
        (MicVisible || DecodeActive) && RealtimeFactor > 0 && RealtimeFactor < 2;

    /// <summary>"Memory · 5.1 GB": the product's whole working set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryChipVisible))]
    public partial string MemoryChip { get; private set; } = "";

    /// <summary>The chips are for testing: off unless opted in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsrChipVisible), nameof(NoteChipVisible), nameof(MemoryChipVisible))]
    public partial bool MetricsVisible { get; set; }

    // Shell, engine and note host: the whole on-device footprint, found by
    // name because the note host is the engine's child process. Polled at
    // 1 Hz while recording, the rate the factor updates at. Failures leave
    // the last value.
    public async Task PollMetricsOnceAsync()
    {
        var memory = await Task.Run(_memoryGb).ConfigureAwait(true);
        MemoryChip = memory > 0
            ? $"Memory · {memory.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)} GB"
            : "";

        if (_engine is null || !_engine.Connected)
        {
            return;
        }

        try
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
        }
        catch (Exception e)
        {
            Log($"engine/metrics failed: {e.Message}");
        }
    }

    partial void OnRealtimeFactorChanged(double value)
    {
        OnPropertyChanged(nameof(RealtimeLow));
        RecomputeChips();
    }

    [ObservableProperty]
    public partial double MicLevel { get; private set; }

    [ObservableProperty]
    public partial bool MicClipped { get; private set; }

    /// <summary>The level bar shows only while audio is flowing.</summary>
    [ObservableProperty]
    public partial bool MicVisible { get; private set; }

    /// <summary>Demo mode is on, or a demo record is on screen. Shown beside the app name.</summary>
    [ObservableProperty]
    public partial bool Demo { get; set; }

    public ObservableCollection<string> LogEntries { get; } = [];

    public void SetMicLevel(double level, bool clipped)
    {
        MicLevel = level;
        MicClipped = clipped;
    }

    public void SetMicVisible(bool visible)
    {
        MicVisible = visible;
        OnPropertyChanged(nameof(RealtimeLow));
        RecomputeChips();
        if (!visible)
        {
            SetMicLevel(0, false);
        }
    }

    private bool _polling;

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

    private EngineStatus _status = EngineStatus.Stopped;
    private EngineFault? _fault;
    private bool _ready;

    /// <summary>True in every transient state. The status ring spins on it.</summary>
    [ObservableProperty]
    public partial bool EngineStarting { get; private set; }

    public void SetEngineState(EngineStatus status, EngineFault? fault)
    {
        _status = status;
        _fault = fault;
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

    /// <summary>Log-only detail, to the file and the developer panel. The displayed status stays concise.</summary>
    public void Log(string line)
    {
        _logger?.Line(line);
        LogEntries.Add(line);
        while (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    public void Append(string line, bool busy = false)
    {
        LogEntries.Add(line);
        while (LogEntries.Count > MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
        }

        _activityBusy = busy;
        LatestActivity = line;
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(Busy));
    }
}
