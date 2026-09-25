using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;
using Ambient.App.Core.Features.Consultation;

namespace Ambient.App.Features.Consultation;

public sealed partial class SessionControlsView : UserControl
{
    public SessionControlsView(SessionControlsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // The ring breathes only while a press would start a recording, and never
        // for someone who has turned animations off in Windows
        StartButton.IsEnabledChanged += (_, _) => Breathe();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionControlsViewModel.IdleVisible))
            {
                Breathe();
            }
        };
        Loaded += (_, _) => Breathe();
        Unloaded += (_, _) => Breathing.Stop();
    }

    public SessionControlsViewModel ViewModel { get; }

    private void Breathe()
    {
        var animate = ViewModel.IdleVisible && StartButton.IsEnabled && new UISettings().AnimationsEnabled;
        if (animate)
        {
            Breathing.Begin();
        }
        else
        {
            Breathing.Stop();
        }
    }
}
