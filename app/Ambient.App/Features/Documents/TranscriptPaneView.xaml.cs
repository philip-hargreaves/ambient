using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Documents;

namespace Ambient.App.Features.Documents;

public sealed partial class TranscriptPaneView : UserControl
{
    public TranscriptPaneView(TranscriptViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        // Presentation only: keep the newest turn in view
        viewModel.Turns.CollectionChanged += OnTurnsChanged;
        // Realised items keep old brushes. A theme change or re-attach after an
        // off-tree change re-realises the list
        SpeakerPalette.Theme = ActualTheme;
        ActualThemeChanged += (_, _) => RefreshStripes();
        Loaded += (_, _) => RefreshStripes();
    }

    private void RefreshStripes()
    {
        if (SpeakerPalette.Theme == ActualTheme)
        {
            return;
        }

        SpeakerPalette.Theme = ActualTheme;
        TurnList.ItemsSource = null;
        TurnList.ItemsSource = ViewModel.Turns;
    }

    public TranscriptViewModel ViewModel { get; }

    /// <summary>Without the heading and its padding, for a host whose tab names the pane.</summary>
    public void ShowHeading(bool shown)
    {
        Heading.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = shown ? new Thickness(16, 12, 16, 8) : new Thickness(0);
    }

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel.Turns.Count > 0)
        {
            TurnList.ScrollIntoView(ViewModel.Turns[^1]);
        }
    }
}
