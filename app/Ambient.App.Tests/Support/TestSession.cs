using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;

namespace Ambient.App.Tests.Support;

internal static class TestSession
{
    /// <summary>A consultation over a quiet fake engine. The status bar is the session's; its lines go to the log when one is given.</summary>
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
