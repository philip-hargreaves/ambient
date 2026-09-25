using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Features.Demo;
using Ambient.App.Features.Documents;

namespace Ambient.App.Features.Consultation;

public sealed partial class ConsultationView : UserControl
{
    public ConsultationView(
        SessionControlsView controls, ReviewSurfaceView surface, DemoTrayView demoTray,
        MicViewModel mic, ConsultationHeaderViewModel header)
    {
        Controls = controls.ViewModel;
        DemoTray = demoTray.ViewModel;
        Mic = mic;
        Header = header;
        InitializeComponent();
        ControlsHost.Content = controls;
        SurfaceHost.Content = surface;
        DemoTrayHost.Content = demoTray;

        // The review appears as the note streams: on the note, with the transcript as
        // the reference, since the guidance follows the note
        Controls.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionControlsViewModel.PanesVisible) && Controls.PanesVisible)
            {
                surface.Open(preferGuidelines: false);
            }
        };
    }

    public SessionControlsViewModel Controls { get; }

    public DemoTrayViewModel DemoTray { get; }

    public MicViewModel Mic { get; }

    public ConsultationHeaderViewModel Header { get; }

    // Refreshed as the flyout opens: a just-plugged headset must appear
    private async void OnMicFlyoutOpening(object sender, object e)
    {
        await Mic.RefreshCommand.ExecuteAsync(null);
        BuildMicFlyout();
    }

    // The box binds on every keystroke, so the view model is current by LostFocus

    private void BuildMicFlyout()
    {
        MicFlyout.Items.Clear();
        if (!Mic.HasDevices)
        {
            MicFlyout.Items.Add(MenuItems.Caption(Mic.NoDevicesText));
            return;
        }

        foreach (var row in Mic.Rows)
        {
            MicFlyout.Items.Add(MenuItems.Radio(row.Label, "mic", row.IsChecked, Mic.SelectCommand, row.Id));
            if (row.NoteVisible)
            {
                MicFlyout.Items.Add(MenuItems.Caption(row.Note));
            }
        }
    }
}
