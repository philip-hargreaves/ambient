using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Shell;

namespace Ambient.App.Shell;

public sealed partial class StatusBarView : UserControl
{
    private readonly CreditsViewModel _credits;

    public StatusBarView(StatusBarViewModel viewModel, CreditsViewModel credits)
    {
        ViewModel = viewModel;
        _credits = credits;
        InitializeComponent();
        // Marks re-resolve on theme change, and on Loaded: a theme switched
        // while this view was off-tree fires no ActualThemeChanged here
        BuildCredits();
        ActualThemeChanged += (_, _) => BuildCredits();
        Loaded += (_, _) => BuildCredits();
    }

    public StatusBarViewModel ViewModel { get; }

    private void BuildCredits() =>
        PartnerMarks.Fill(CreditsRow, _credits.Marks, ActualTheme == ElementTheme.Dark);
}
