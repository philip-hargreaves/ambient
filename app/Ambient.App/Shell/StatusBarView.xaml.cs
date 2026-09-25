using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Ambient.App.Controls;
using Ambient.App.Core.Shell;

namespace Ambient.App.Shell;

public sealed partial class StatusBarView : UserControl
{
    private static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    public StatusBarView(StatusBarViewModel viewModel, CreditsViewModel credits)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PartnerMarks.Attach(this, CreditsRow, credits.Marks);
        Loaded += (_, _) => FitChips();
        // A chip's words change its width, so the fit is checked again
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StatusBarViewModel.AsrChip)
                or nameof(StatusBarViewModel.NoteChip)
                or nameof(StatusBarViewModel.MemoryChip)
                or nameof(StatusBarViewModel.MetricsVisible)
                or nameof(StatusBarViewModel.DisplayLabel))
            {
                FitChips();
            }
        };
    }

    public StatusBarViewModel ViewModel { get; }

    private void OnBarSizeChanged(object sender, SizeChangedEventArgs e) => FitChips();

    // The state line and the partner marks always fit; the chips show only when the
    // room between them holds all of them, so nothing is ever clipped
    private void FitChips()
    {
        if (Bar.ActualWidth <= 0)
        {
            return;
        }

        Chips.Visibility = Visibility.Visible;
        Chips.Measure(Unbounded);
        State.Measure(Unbounded);
        CreditsRow.Measure(Unbounded);
        var room = Bar.ActualWidth - State.DesiredSize.Width - CreditsRow.DesiredSize.Width - 3 * Bar.ColumnSpacing;
        Chips.Visibility = Chips.DesiredSize.Width <= room ? Visibility.Visible : Visibility.Collapsed;
    }
}
