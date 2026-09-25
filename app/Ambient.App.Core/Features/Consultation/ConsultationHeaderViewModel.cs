using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.App.Core.Common;

namespace Ambient.App.Core.Features.Consultation;

/// <summary>
/// The heading over the live review: when the consultation started, in the words the inbox
/// lists it with. The engine's label shows in Sessions, so the heading never changes underfoot.
/// </summary>
public sealed partial class ConsultationHeaderViewModel : ObservableObject
{
    private readonly ConsultationViewModel _session;
    private readonly Func<DateTimeOffset> _clock;
    private string _id = "";

    public ConsultationHeaderViewModel(ConsultationViewModel session, Func<DateTimeOffset>? clock = null)
    {
        _session = session;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConsultationViewModel.State))
            {
                OnStateChanged();
            }
        };
    }

    /// <summary>"Thursday 25 September, 15:09", set as a recording starts.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    private void OnStateChanged()
    {
        var id = _session.Recorder.RecordingSessionId ?? "";
        if (_session.State == SessionState.Recording && id != _id)
        {
            _id = id;
            Title = SessionText.Heading(_clock());
        }
    }
}
