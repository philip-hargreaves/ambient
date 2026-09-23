using System.ComponentModel;
using Ambient.App.Core.Features.Consultation;

namespace Ambient.App.Core.Hosting;

/// <summary>
/// The session as the engine host sees it, mirrored from the consultation view model. The
/// host is built before the view model, so this stands between them instead of a cycle.
/// </summary>
public sealed class LiveSessionState : ISessionState
{
    public bool ConsultationActive { get; private set; }

    public string SessionPhase { get; private set; } = "";

    public void Follow(ConsultationViewModel session)
    {
        Mirror(session);
        session.PropertyChanged += (_, e) => OnChanged(session, e);
    }

    private void OnChanged(ConsultationViewModel session, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConsultationViewModel.State) or nameof(ConsultationViewModel.Phase))
        {
            Mirror(session);
        }
    }

    private void Mirror(ConsultationViewModel session)
    {
        ConsultationActive = session.ConsultationActive;
        SessionPhase = session.SessionPhase;
    }
}
