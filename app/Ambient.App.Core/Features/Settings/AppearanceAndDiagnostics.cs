using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Metrics;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Features.Settings;

/// <summary>
/// The theme, the transcription device, and the developer tools: the demo controls, the
/// metrics chips, the performance log and its report.
/// </summary>
public sealed partial class AppearanceAndDiagnostics : ObservableObject
{
    private readonly AppPreferences? _preferences;
    private readonly IEngineHost? _engine;
    private readonly ISessionState? _session;
    private readonly StatusBarViewModel? _status;
    private readonly IMachineInfoProvider? _machine;
    private readonly PerformanceCollector? _metrics;
    private readonly DemoMode? _demo;
    private readonly IFilePicker? _picker;
    private readonly IThemeService? _theme;
    private readonly string _exportDirectory;
    private readonly bool _initialising;
    private bool _reverting;

    public AppearanceAndDiagnostics(
        AppPreferences? preferences, IEngineHost? engine, ISessionState? session,
        StatusBarViewModel? status, IMachineInfoProvider? machine, PerformanceCollector? metrics,
        string? exportDirectory, DemoMode? demo, IFilePicker? picker, IThemeService? theme)
    {
        _preferences = preferences;
        _engine = engine;
        _session = session;
        _status = status;
        _machine = machine;
        _metrics = metrics;
        _demo = demo;
        _picker = picker;
        _theme = theme;
        _exportDirectory = exportDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        // Restoring saved values is not the clinician changing them: the
        // handlers (persist, apply, engine restart) must not fire here
        _initialising = true;
        DemoTrayEnabled = preferences?.DemoTrayEnabled ?? false;
        DemoModeEnabled = demo?.Enabled ?? false;
        NpuTranscription = preferences?.NpuTranscription ?? false;
        CollectPerformanceData = preferences?.CollectPerformanceData ?? false;
        ShowPerformanceMetrics = preferences?.ShowPerformanceMetrics ?? false;
        Theme = preferences?.Theme ?? "system";
        _initialising = false;
    }

    /// <summary>"system", "light" or "dark". The shell applies it live.</summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "system";

    partial void OnThemeChanged(string value)
    {
        OnPropertyChanged(nameof(ThemeIndex));
        if (_initialising)
        {
            return;
        }

        _theme?.Apply(value);
        if (_preferences is not null)
        {
            _preferences.Theme = value;
            _preferences.Save();
        }
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System default", "Light", "Dark"];

    /// <summary>Theme as the Appearance control's selection, same order.</summary>
    public int ThemeIndex
    {
        get => Math.Max(0, AppPreferences.Themes.ToList().IndexOf(Theme));
        set => Theme = AppPreferences.Themes[Math.Clamp(value, 0, AppPreferences.Themes.Count - 1)];
    }

    /// <summary>Runs transcription on the NPU. The engine restarts to apply it.</summary>
    [ObservableProperty]
    public partial bool NpuTranscription { get; set; }

    partial void OnNpuTranscriptionChanged(bool value)
    {
        if (_reverting || _initialising)
        {
            return;
        }

        // A restart would kill a live session
        if (_session?.ConsultationActive == true)
        {
            _reverting = true;
            NpuTranscription = !value;
            _reverting = false;
            _status?.Append("finish the consultation before switching transcription device");
            return;
        }

        if (_preferences is not null)
        {
            _preferences.NpuTranscription = value;
            _preferences.Save();
        }

        if (_engine is not null)
        {
            _status?.Append(value
                ? "preparing the low-power model - the first switch can take a few minutes"
                : "switching speech recognition to the GPU", busy: true);
            _engine.Shutdown();
            _engine.Start();
        }
    }

    // ---- developer tools

    /// <summary>The Developer tools group, closed on every launch.</summary>
    [ObservableProperty]
    public partial bool DeveloperToolsExpanded { get; set; }

    /// <summary>Shows the replay tray. A developer control.</summary>
    [ObservableProperty]
    public partial bool DemoTrayEnabled { get; set; }

    partial void OnDemoTrayEnabledChanged(bool value)
    {
        if (!_initialising && _preferences is not null)
        {
            _preferences.DemoTrayEnabled = value;
            _preferences.Save();
        }
    }

    /// <summary>Demo mode: Record plays a saved run back. A developer control.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemoTrackOptions))]
    [NotifyPropertyChangedFor(nameof(DemoTrackIndex))]
    public partial bool DemoModeEnabled { get; set; }

    partial void OnDemoModeEnabledChanged(bool value)
    {
        if (!_initialising && _demo is not null)
        {
            _demo.Enabled = value;
        }
    }

    /// <summary>The tracks with a saved run, as the picker's items.</summary>
    public IReadOnlyList<string> DemoTrackOptions => _demo?.Tracks ?? [];

    public bool DemoTracksAvailable => DemoTrackOptions.Count > 0;

    public string DemoModeCaption => DemoTracksAvailable
        ? "Record plays the chosen saved run back in seconds; nothing is transcribed or written"
        : "No saved runs yet: record them with tools/demo/record_masters.py";

    /// <summary>The chosen track as the picker's selection. An unknown track falls back to the first.</summary>
    public int DemoTrackIndex
    {
        get => Math.Max(0, DemoTrackOptions.ToList().IndexOf(_demo?.Track ?? ""));
        set
        {
            if (_demo is not null && value >= 0 && value < DemoTrackOptions.Count)
            {
                _demo.Track = DemoTrackOptions[value];
            }
        }
    }

    /// <summary>Shows the status-bar model and memory chips. For testing.</summary>
    [ObservableProperty]
    public partial bool ShowPerformanceMetrics { get; set; }

    partial void OnShowPerformanceMetricsChanged(bool value)
    {
        if (_initialising)
        {
            return;
        }

        if (_status is not null)
        {
            _status.MetricsVisible = value;
        }

        if (_preferences is not null)
        {
            _preferences.ShowPerformanceMetrics = value;
            _preferences.Save();
        }
    }

    /// <summary>Local performance collection: numbers and device names only.</summary>
    [ObservableProperty]
    public partial bool CollectPerformanceData { get; set; }

    partial void OnCollectPerformanceDataChanged(bool value)
    {
        if (!_initialising && _preferences is not null)
        {
            _preferences.CollectPerformanceData = value;
            _preferences.Save();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDescription))]
    public partial string ExportResult { get; private set; } = "";

    /// <summary>The Export row's line: the last outcome once there is one.</summary>
    public string ExportDescription =>
        ExportResult.Length > 0 ? ExportResult : "Saves the report as an HTML file";

    /// <summary>One self-contained HTML file: readable, emailable, parseable.</summary>
    [RelayCommand]
    private async Task ExportPerformanceReport()
    {
        if (_machine is null || _metrics is null || !File.Exists(_metrics.Path))
        {
            ExportResult = "no performance data collected yet";
            return;
        }

        try
        {
            var html = ReportBuilder.Build(
                _machine.Describe(), File.ReadAllLines(_metrics.Path), DateTimeOffset.UtcNow);
            var suggested = $"ambient-perf-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}.html";
            var path = _picker is not null
                ? await _picker.PickSaveAsync(suggested, "HTML report", ".html").ConfigureAwait(true)
                : Path.Combine(_exportDirectory, suggested);
            if (path is null)
            {
                return;  // cancelled: no file, no caption
            }

            File.WriteAllText(path, html);
            ExportResult = $"saved {path}";
        }
        catch (Exception e)
        {
            ExportResult = $"export failed: {e.Message}";
        }
    }
}
