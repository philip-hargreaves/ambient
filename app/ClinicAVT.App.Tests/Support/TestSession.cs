using ClinicAVT.App.Core.Features.Consultation;
using ClinicAVT.App.Core.Features.Demo;
using ClinicAVT.App.Core.Features.Documents;
using ClinicAVT.App.Core.Features.Guidance;
using ClinicAVT.App.Core.Preferences;
using ClinicAVT.App.Core.Shell;
using ClinicAVT.App.Tests.TestDoubles;
using ClinicAVT.Client;

namespace ClinicAVT.App.Tests.Support;

internal static class TestSession
{
    /// <summary>A consultation over a quiet fake engine. The status bar is the session's own, and its lines go to the log when one is given.</summary>
    public static (ConsultationViewModel Session, FakeEngineClient Engine, NoteViewModel Note) Create(
        AppPreferences? preferences = null, FakeDialogService? dialogs = null,
        FakeEngineClient? engine = null, DemoMode? demo = null, TimeSpan? readinessPollInterval = null,
        ListLogger? log = null)
    {
        engine ??= new FakeEngineClient(autoNotify: false);
        var note = new NoteViewModel();
        var status = new StatusBarViewModel(log);
        var session = new ConsultationViewModel(
            new EngineApi(engine), new InlineDispatcher(), new TranscriptViewModel(), note, status,
            dialogs ?? new FakeDialogService(), Page(engine, status), Guidance(status),
            readinessPollInterval: readinessPollInterval, preferences: preferences, demo: demo);
        return (session, engine, note);
    }

    public static PageViewModel Page(IEngineTransport engine, StatusBarViewModel status) =>
        new(new EngineApi(engine), new FakeLauncher(), new FakeClipboard(), status);

    public static GuidanceViewModel Guidance(StatusBarViewModel status) =>
        new(new FakeLauncher(), new FakeClipboard(), status);

    public static MicViewModel Mic() => new(new EngineApi(new FakeEngineClient()));
}
