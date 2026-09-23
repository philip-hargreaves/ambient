using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Demo;

namespace Ambient.App.Features.Demo;

public sealed partial class DemoTrayView : UserControl
{
    public DemoTrayView(DemoTrayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ReplayFlyout.Opening += (_, _) => BuildFlyout();
    }

    public DemoTrayViewModel ViewModel { get; }

    // Rebuilt on open: the recordings and Browse
    private void BuildFlyout()
    {
        ReplayFlyout.Items.Clear();
        foreach (var track in ViewModel.Tracks)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = track.Display,
                GroupName = "track",
                IsChecked = ReferenceEquals(track, ViewModel.SelectedTrack),
            };
            var chosen = track;
            item.Click += (_, _) => ViewModel.SelectedTrack = chosen;
            ReplayFlyout.Items.Add(item);
        }

        ReplayFlyout.Items.Add(new MenuFlyoutItem
        {
            Text = "Browse for a recording...",
            Command = ViewModel.BrowseCommand,
        });
    }
}
