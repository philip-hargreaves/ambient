using Microsoft.UI.Xaml.Controls;
using ClinicAVT.App.Controls;
using ClinicAVT.App.Core.Features.Demo;

namespace ClinicAVT.App.Features.Demo;

public sealed partial class DemoTrayView : UserControl
{
    public DemoTrayView(DemoTrayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ReplayFlyout.Opening += (_, _) => BuildFlyout();
    }

    public DemoTrayViewModel ViewModel { get; }

    private void BuildFlyout()
    {
        ReplayFlyout.Items.Clear();
        foreach (var track in ViewModel.Tracks)
        {
            var chosen = track;
            ReplayFlyout.Items.Add(MenuItems.Radio(
                track.Display, "track", ReferenceEquals(track, ViewModel.SelectedTrack),
                () => ViewModel.SelectedTrack = chosen));
        }

        ReplayFlyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Browse for a recording...",
            Command = ViewModel.BrowseCommand,
        });
    }
}
