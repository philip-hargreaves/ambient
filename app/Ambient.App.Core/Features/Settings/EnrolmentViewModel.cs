using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Common;
using Ambient.App.Core.Ports;
using Ambient.Client;

namespace Ambient.App.Core.Features.Settings;

/// <summary>
/// One reading of the passage: Start, read at your own pace, Finish, then the
/// engine's verdict. The dialog binds to this and closes on Succeeded.
/// </summary>
public sealed partial class EnrolmentViewModel : ObservableObject, IDisposable
{
    /// <summary>A cap the reader never sees. Finish is how a reading ends.</summary>
    public const double DefaultSeconds = 120;

    /// <summary>What the engine needs before it will make a print.</summary>
    public const double NeededSpeechSeconds = 20;

    /// <summary>
    /// One sentence that says what is happening, then the clinician's own
    /// register: questions and a plan. About thirty seconds at a natural pace,
    /// which leaves a margin over the 20 s of speech the engine needs.
    /// </summary>
    public const string Passage =
        "I am reading this so Ambient learns my voice and can tell me apart from my patients. "
        + "Good morning, thanks for coming in. Have you had any chest pain, shortness of breath "
        + "or dizziness in the last two weeks? Are you taking any regular medication? I will "
        + "check your blood pressure and listen to your chest, and then we can talk about what "
        + "happens next. Do you have any questions before we start?";

    private readonly IEngineApi _engine;
    private readonly IUiDispatcher? _dispatcher;
    private readonly string _micId;
    private readonly double _seconds;
    private readonly TaskCompletionSource<bool> _outcome =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public EnrolmentViewModel(IEngineApi engine, string micId = "",
        double seconds = DefaultSeconds, IUiDispatcher? dispatcher = null)
    {
        _engine = engine;
        _micId = micId;
        _seconds = seconds;
        _dispatcher = dispatcher;
        _engine.NotificationReceived += OnNotification;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine), nameof(PrimaryText), nameof(CloseText),
        nameof(Recording), nameof(Succeeded), nameof(KeepsOpen))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CancelCommand), nameof(FinishCommand))]
    public partial EnrolmentState State { get; private set; } = EnrolmentState.Ready;

    /// <summary>Microphone level, 0 to 1, for the ring.</summary>
    [ObservableProperty]
    public partial double Level { get; private set; }

    /// <summary>Clear speech captured so far, in seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress), nameof(StatusLine), nameof(EnoughCaptured))]
    public partial double Speech { get; private set; }

    /// <summary>The engine's reason when the reading was not enough.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    public partial string Detail { get; private set; } = "";

    /// <summary>The passage, for binding.</summary>
    public string PassageText { get; } = Passage;

    public bool Recording => State == EnrolmentState.Recording;

    public bool Succeeded => State == EnrolmentState.Succeeded;

    /// <summary>Clear speech captured against what the engine needs, 0 to 1.</summary>
    public double Progress => Math.Clamp(Speech / NeededSpeechSeconds, 0, 1);

    public bool EnoughCaptured => Speech >= NeededSpeechSeconds;

    public string StatusLine => State switch
    {
        EnrolmentState.Ready => "Press Start, then read the passage aloud.",
        EnrolmentState.Recording when EnoughCaptured =>
            "Enough captured. Finish whenever you reach the end.",
        EnrolmentState.Recording => "Listening. Read to the end, then press Finish.",
        EnrolmentState.Succeeded => "Voice enrolment complete.",
        _ => Detail.Length > 0 ? $"That did not work: {Detail}." : "That did not work.",
    };

    public string PrimaryText => State switch
    {
        EnrolmentState.Ready => "Start",
        EnrolmentState.Recording => "Finish",
        EnrolmentState.Succeeded => "Done",
        _ => "Try again",
    };

    public string CloseText => State switch
    {
        EnrolmentState.Succeeded => "",
        EnrolmentState.Failed => "Close",
        _ => "Cancel",
    };

    /// <summary>Start, Finish and Try again keep the dialog open; only Done closes it.</summary>
    public bool KeepsOpen => State != EnrolmentState.Succeeded;

    /// <summary>True once a print was made. False on cancel, failure or dismissal.</summary>
    public Task<bool> Outcome => _outcome.Task;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        State = EnrolmentState.Recording;
        Speech = 0;
        Detail = "";
        try
        {
            await _engine.StartEnrolmentAsync(_seconds, _micId).ConfigureAwait(true);
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    private bool CanStart() =>
        State is EnrolmentState.Ready or EnrolmentState.Failed && _engine.Connected;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Cancel()
    {
        try
        {
            await _engine.CancelEnrolmentAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The engine will report the outcome, or the dialog is closing anyway
        }
    }

    private bool CanCancel() => State == EnrolmentState.Recording;

    /// <summary>The reader reached the end: the engine makes the print from what it heard.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Finish()
    {
        try
        {
            await _engine.FinishEnrolmentAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
    }

    /// <summary>The primary button: Start, then Finish, then Try again. Done does nothing but close.</summary>
    [RelayCommand]
    private Task Primary() => State switch
    {
        EnrolmentState.Recording => Finish(),
        EnrolmentState.Succeeded => Task.CompletedTask,
        _ => Start(),
    };

    /// <summary>The dialog was dismissed. A reading in flight is cancelled.</summary>
    public void Dismiss()
    {
        if (State == EnrolmentState.Recording)
        {
            _ = Cancel();
        }

        _outcome.TrySetResult(State == EnrolmentState.Succeeded);
    }

    public void Dispose()
    {
        _engine.NotificationReceived -= OnNotification;
        _outcome.TrySetResult(State == EnrolmentState.Succeeded);
    }

    private void OnNotification(EngineNotification notification)
    {
        switch (notification)
        {
            case EnrolmentProgress progress:
                _dispatcher.PostOrRun(() => Apply(progress));
                break;
            case EnrolmentDone done:
                _dispatcher.PostOrRun(() => Apply(done));
                break;
            default:
                break;
        }
    }

    private void Apply(EnrolmentProgress progress)
    {
        if (State == EnrolmentState.Recording)
        {
            Level = progress.Level;
            Speech = progress.Speech;
        }
    }

    private void Apply(EnrolmentDone done)
    {
        Level = 0;
        if (done.Ok)
        {
            State = EnrolmentState.Succeeded;
            _outcome.TrySetResult(true);
        }
        else
        {
            Fail(done.Detail ?? "");
        }
    }

    private void Fail(string detail)
    {
        Detail = detail;
        State = EnrolmentState.Failed;
    }
}
