using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Features.Demo;

/// <summary>
/// The dev-only replay transport: bundled tracks, play/pause/stop, speed,
/// monitor audio and progress. Never visible to a clinician.
/// </summary>
public sealed partial class DemoTrayViewModel : ObservableObject
{
    private static readonly double[] Speeds = [1, 4, 8, 16];

    private readonly ConsultationViewModel _session;
    private readonly IFilePicker _picker;

    public DemoTrayViewModel(
        ConsultationViewModel session, IFilePicker picker, IReadOnlyList<DemoTrack>? tracks = null)
    {
        _session = session;
        _picker = picker;
        Tracks = new List<DemoTrack>(tracks ?? DemoTracks.Load());
        SelectedTrack = Tracks.FirstOrDefault();
        _session.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ConsultationViewModel.State):
                case nameof(ConsultationViewModel.EngineReady):
                    OnPropertyChanged(nameof(IsReplaying));
                    OnPropertyChanged(nameof(Idle));
                    PlayCommand.NotifyCanExecuteChanged();
                    StopCommand.NotifyCanExecuteChanged();
                    TogglePauseCommand.NotifyCanExecuteChanged();
                    break;
                case nameof(ConsultationViewModel.Paused):
                    OnPropertyChanged(nameof(PauseGlyph));
                    break;
                case nameof(ConsultationViewModel.AudioSeconds):
                    OnPropertyChanged(nameof(ProgressFraction));
                    OnPropertyChanged(nameof(ProgressText));
                    break;
                default:
                    break;
            }
        };
    }

    public List<DemoTrack> Tracks { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackName))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ProgressFraction))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    public partial DemoTrack? SelectedTrack { get; set; }

    // Read once per selection, so progress ticks do not reread it
    private double _durationSeconds;

    partial void OnSelectedTrackChanged(DemoTrack? value) =>
        _durationSeconds = value is null ? 0 : DemoTracks.DurationSeconds(value.Path);

    public string TrackName => SelectedTrack?.Name ?? "no track";

    [RelayCommand]
    private async Task Browse()
    {
        var path = await _picker.PickFileAsync(".wav").ConfigureAwait(true);
        if (path is not null)
        {
            UseTrack(path);
        }
    }

    /// <summary>A browsed file becomes a selectable track named after itself.</summary>
    public void UseTrack(string path)
    {
        var track = new DemoTrack(Path.GetFileNameWithoutExtension(path), path);
        Tracks.Add(track);
        OnPropertyChanged(nameof(Tracks));
        SelectedTrack = track;
    }

    // ---- speed: cycles 1 -> 4 -> 8 -> 16. Anything over 1x is for smoke tests only

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedLabel))]
    [NotifyPropertyChangedFor(nameof(IsSmoke))]
    public partial double Speed { get; set; } = 1;

    public bool IsSmoke => Speed > 1;

    public string SpeedLabel => $"{Speed:0}×";

    [RelayCommand]
    private void CycleSpeed()
    {
        var i = Array.IndexOf(Speeds, Speed);
        Speed = Speeds[(i < 0 ? 0 : i + 1) % Speeds.Length];
    }

    // ---- monitor audio

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorGlyph))]
    public partial bool MonitorAudio { get; set; }

    // Mid-replay the toggle takes effect immediately
    partial void OnMonitorAudioChanged(bool value)
    {
        if (IsReplaying)
        {
            _ = _session.SetMonitorAsync(value);
        }
    }

    public string MonitorGlyph => MonitorAudio ? "\uE767" : "\uE74F";  // volume / mute

    // ---- transport

    public bool IsReplaying => _session.State == SessionState.Recording
        && _session.ActiveReplay is not null;

    /// <summary>Replay controls show only while idle.</summary>
    public bool Idle => _session.State == SessionState.Idle;

    public string PauseGlyph => _session.Paused ? "\uE768" : "\uE769";  // play / pause

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task Play() => _session.StartRecordingAsync(
        new ReplayRequest(SelectedTrack!.Path, Speed, MonitorAudio));

    private bool CanPlay() => _session.State == SessionState.Idle && _session.EngineReady
        && SelectedTrack is not null;

    [RelayCommand(CanExecute = nameof(IsReplaying))]
    private Task Stop() => _session.StopRecordingAsync();

    [RelayCommand(CanExecute = nameof(IsReplaying))]
    private Task TogglePause() => _session.SetPausedAsync(!_session.Paused);

    // ---- progress, from delivered audio against the wav's own duration

    public double ProgressFraction => _durationSeconds <= 0
        ? 0
        : Math.Min(1.0, _session.AudioSeconds / _durationSeconds);

    public string ProgressText => $"{Clock(_session.AudioSeconds)} / {Clock(_durationSeconds)}";

    private static string Clock(double seconds)
    {
        var whole = (int)Math.Round(seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }
}
