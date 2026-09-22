using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Support;

internal static class TestSession
{
    public static (ConsultationViewModel Session, FakeEngineClient Engine, NoteViewModel Note)
        Create(AppPreferences? preferences = null)
    {
        var engine = new FakeEngineClient(autoNotify: false);
        var note = new NoteViewModel();
        var session = new ConsultationViewModel(
            engine, new InlineDispatcher(), new TranscriptViewModel(), note,
            new StatusBarViewModel(), preferences: preferences);
        return (session, engine, note);
    }
}
