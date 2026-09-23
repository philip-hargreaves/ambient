using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Ports;

namespace Ambient.App.Features.Demo;

public sealed partial class DemoTrayView : UserControl
{
    private readonly IFilePicker _picker;

    public DemoTrayView(DemoTrayViewModel viewModel, IFilePicker picker)
    {
        ViewModel = viewModel;
        _picker = picker;
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

        var browse = new MenuFlyoutItem { Text = "Browse for a recording..." };
        browse.Click += OnBrowse;
        ReplayFlyout.Items.Add(browse);
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var path = await _picker.PickFileAsync(".wav");
        if (path is not null)
        {
            ViewModel.UseTrack(path);
        }
    }
}
