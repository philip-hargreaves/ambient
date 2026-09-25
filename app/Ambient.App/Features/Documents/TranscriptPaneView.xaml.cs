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
        viewModel.Turns.CollectionChanged += OnTurnsChanged;
    }

    public TranscriptViewModel ViewModel { get; }

    /// <summary>
    /// Beside the note the turns stop where the note column does and scroll past it, so the
    /// columns end together. Infinity lifts the cap when the pane has the area to itself.
    /// </summary>
    public void CapHeight(double maxHeight)
    {
        var capped = !double.IsPositiveInfinity(maxHeight);
        TurnList.MaxHeight = capped ? Math.Max(160, maxHeight) : double.PositiveInfinity;
        TurnList.VerticalAlignment = capped ? VerticalAlignment.Top : VerticalAlignment.Stretch;
    }

    /// <summary>Without the heading and its padding, for a host whose tab names the pane.</summary>
    public void ShowHeading(bool shown)
    {
        Heading.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Root.Padding = shown ? new Thickness(16, 12, 16, 8) : new Thickness(0);
    }

    // Keeps the newest turn in view
    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel.Turns.Count > 0)
        {
            TurnList.ScrollIntoView(ViewModel.Turns[^1]);
        }
    }
}
