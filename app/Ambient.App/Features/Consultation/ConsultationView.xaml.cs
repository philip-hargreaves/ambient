using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Demo;
using Ambient.App.Features.Documents;
using Ambient.App.Features.Guidance;
using Ambient.App.Shell;

namespace Ambient.App.Features.Consultation;

public sealed partial class ConsultationView : UserControl
{
    public ConsultationView(
        ShellViewModel shell, SessionControlsView controls, TranscriptPaneView transcript,
        NotePaneView note, StatusBarView status, DemoTrayView demoTray, SettingsViewModel settings,
        MicViewModel mic, ConsultationViewModel consultation, PageView page)
    {
        Shell = shell;
        Controls = controls.ViewModel;
        Mic = mic;
        InitializeComponent();
        // Refreshed as the flyout opens: a just-plugged headset must appear
        MicFlyout.Opening += async (_, _) =>
        {
            await Mic.RefreshAsync();
            BuildMicFlyout();
        };

        ControlsHost.Content = controls;
        TranscriptHost.Content = transcript;
        NoteHost.Content = note;
        PageHost.Content = page;
        StatusHost.Content = status;
        DemoTrayHost.Content = demoTray;

        void PlacePage()
        {
            var open = consultation.PageView.Visible;
            var wide = new GridLength(1.15, GridUnitType.Star);
            TranscriptColumn.Width = open ? new GridLength(0) : wide;
            PageColumn.Width = open ? wide : new GridLength(0);
            TranscriptHost.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            PageHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }
        PlacePage();
        consultation.PageView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageViewModel.Visible))
            {
                PlacePage();
            }
        };

        // The tray exists only while the settings toggle says so
        void Apply() => DemoTrayHost.Visibility =
            settings.DemoTrayEnabled ? Visibility.Visible : Visibility.Collapsed;
        Apply();
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.DemoTrayEnabled))
            {
                Apply();
            }
        };
    }

    public ShellViewModel Shell { get; }

    public SessionControlsViewModel Controls { get; }

    public MicViewModel Mic { get; }

    private void BuildMicFlyout()
    {
        MicFlyout.Items.Clear();
        if (!Mic.HasDevices)
        {
            MicFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = "No microphone found - connect one to record",
                IsEnabled = false,
            });
            return;
        }

        foreach (var device in Mic.Devices)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = device.IsDefault ? $"{device.Name}  (default)" : device.Name,
                GroupName = "mic",
                IsChecked = device.Id == Mic.MicId,
            };
            var id = device.Id;
            item.Click += (_, _) => Mic.Select(id);
            MicFlyout.Items.Add(item);
            if (device.Bluetooth)
            {
                // Quality warning next to the choice it concerns
                MicFlyout.Items.Add(new MenuFlyoutItem
                {
                    Text = "    Bluetooth call mode - reduced recording quality",
                    IsEnabled = false,
                });
            }
        }
    }
}
