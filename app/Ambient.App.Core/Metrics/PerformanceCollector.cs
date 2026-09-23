using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Metrics;

/// <summary>
/// Appends one JSON line per finished session to metrics.jsonl: the engine's
/// metrics snapshot plus shell-side timings for the clinical note and the
/// patient note, the note model, and memory peaks. Numbers and device names
/// only, never content. Writes nothing unless enabled.
/// </summary>
public sealed class PerformanceCollector(
    IEngineApi engine, Func<bool> enabled, Func<int?> enginePid, string path,
    IProcessMetrics? processes = null, Func<PowerState>? power = null, ILogger? logger = null)
{
    private readonly IProcessMetrics _processes = processes ?? new NoProcessMetrics();
    private readonly Func<PowerState> _power = power ?? (() => PowerState.Unknown);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private DateTimeOffset _start;
    private string _source = "";
    private double _replaySpeed;
    private string? _track;
    private long? _availableAtStartMb;
    private Stopwatch? _stopClock;
    private double? _noteFirstPartial;
    private double? _noteReady;
    private double? _noteRate;
    private double? _patientFirstPartial;
    private double? _patientRate;
    private string? _modelName;
    private string? _modelTier;
    private double? _modelLoadSeconds;

    public string Path { get; } = path;

    /// <summary>The note model the engine reports resident; remembered across sessions.</summary>
    public void NoteModel(string? name, string? tier, double? loadSeconds)
    {
        _modelName = name;
        _modelTier = tier;
        _modelLoadSeconds = loadSeconds;
    }

    public void SessionStarted(string source, double replaySpeed, string? track)
    {
        if (!enabled())
        {
            _stopClock = null;
            return;
        }

        _start = DateTimeOffset.UtcNow;
        _source = source;
        _replaySpeed = replaySpeed;
        _track = track;
        _availableAtStartMb = AvailableMemoryMb();
        _stopClock = null;
        _noteFirstPartial = _noteReady = _noteRate = null;
        _patientFirstPartial = _patientRate = null;
    }

    public void StopRequested()
    {
        _stopClock ??= Stopwatch.StartNew();
    }

    public void NotePartial(double? tokensPerSecond = null)
    {
        if (_stopClock is not null)
        {
            _noteFirstPartial ??= _stopClock.Elapsed.TotalSeconds;
            _noteRate = tokensPerSecond ?? _noteRate;
        }
    }

    /// <summary>The clinical note is complete; the patient note's clock starts here.</summary>
    public void NoteReady(double? tokensPerSecond = null)
    {
        if (_stopClock is not null)
        {
            _noteReady ??= _stopClock.Elapsed.TotalSeconds;
            _noteRate = tokensPerSecond ?? _noteRate;
        }
    }

    public void PatientPartial(double? tokensPerSecond = null)
    {
        if (_stopClock is not null)
        {
            _patientFirstPartial ??= _stopClock.Elapsed.TotalSeconds;
            _patientRate = tokensPerSecond ?? _patientRate;
        }
    }

    /// <summary>
    /// Fetches the engine snapshot and appends the session's line. Called when
    /// the patient note is ready, or as soon as either note is refused or fails.
    /// </summary>
    public async Task SessionFinishedAsync(string? noteFailure, int noteChars,
        string? patientFailure = null, double? patientTokensPerSecond = null)
    {
        if (!enabled() || _stopClock is null)
        {
            return;
        }

        var stopClock = _stopClock;
        _stopClock = null;
        var now = stopClock.Elapsed.TotalSeconds;
        JsonElement? engineMetrics = null;
        try
        {
            engineMetrics = (await engine.MetricsAsync().ConfigureAwait(false)).Raw;
        }
        catch (Exception e)
        {
            logger?.StepFailed("engine/metrics at session end", e.Message);
        }

        double? noteReady = noteFailure is null ? _noteReady ?? now : null;
        var outcome = noteFailure is not null
            ? (noteFailure.StartsWith("refused", StringComparison.Ordinal) ? "refused" : "clinical note failed")
            : patientFailure is not null ? "patient note failed" : "completed";
        var record = new
        {
            schema = 2,
            start = _start,
            source = _source,
            replaySpeed = _replaySpeed > 0 ? (double?)_replaySpeed : null,
            track = _track,
            outcome,
            engine = engineMetrics,
            note = new
            {
                model = _modelName,
                tier = _modelTier,
                modelLoadSeconds = Round(_modelLoadSeconds),
                firstPartialAfterStopSeconds = Round(_noteFirstPartial),
                readyAfterStopSeconds = Round(noteReady),
                tokensPerSecond = _noteRate,
                chars = noteChars,
                failed = noteFailure,
            },
            // Timed from the clinical note's completion: it cannot start before
            patient = noteFailure is null
                ? new
                {
                    firstPartialAfterNoteSeconds = Round(Since(noteReady, _patientFirstPartial)),
                    readyAfterNoteSeconds = patientFailure is null ? Round(Since(noteReady, now)) : null,
                    tokensPerSecond = patientTokensPerSecond ?? _patientRate,
                    failed = patientFailure,
                }
                : null,
            // The power situation at stop: it decides the finalise floor
            power = _power(),
            memory = new
            {
                availableAtStartMb = _availableAtStartMb,
                peakWorkingSetMb = EngineMemoryMb(_processes.PeakWorkingSetMb),
                peakCommitMb = EngineMemoryMb(_processes.PeakCommitMb),
                noteHostPeakWorkingSetMb = NoteHostPeakMb(),
            },
        };

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, JsonSerializer.Serialize(record, Json) + Environment.NewLine);
        }
        catch (IOException e)
        {
            logger?.StepFailed("metrics line write", e.Message);
        }
    }

    private static double? Since(double? from, double? at) =>
        from is null || at is null ? null : Math.Max(0, at.Value - from.Value);

    private static double? Round(double? seconds) =>
        seconds is null ? null : Math.Round(seconds.Value, 2);

    private long? EngineMemoryMb(Func<int, long?> metric) =>
        enginePid() is { } pid ? metric(pid) : null;

    // The note model lives in its own process beside the engine
    private long? NoteHostPeakMb() =>
        enginePid() is null ? null : _processes.PeakWorkingSetMbOf(EngineLayout.NoteHostProcess);

    private static long? AvailableMemoryMb()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            return (info.TotalAvailableMemoryBytes - info.MemoryLoadBytes) / (1024 * 1024);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
