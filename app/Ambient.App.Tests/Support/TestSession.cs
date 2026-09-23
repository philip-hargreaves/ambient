using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Support;

internal static class TestSession
{
    public static (ConsultationViewModel Session, FakeEngineClient Engine, NoteViewModel Note)
        Create(AppPreferences? preferences = null, FakeDialogService? dialogs = null)
    {
        var engine = new FakeEngineClient(autoNotify: false);
        var note = new NoteViewModel();
        var status = new StatusBarViewModel();
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), note, status,
            dialogs ?? new FakeDialogService(), Page(engine, status), preferences: preferences);
        return (session, engine, note);
    }

    public static PageViewModel Page(Ambient.Client.IEngineClient engine, StatusBarViewModel status) =>
        new(engine, new FakeLauncher(), new FakeClipboard(), status);
}
