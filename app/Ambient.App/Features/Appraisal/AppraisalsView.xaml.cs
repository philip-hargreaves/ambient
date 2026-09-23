using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Appraisal;

/// <summary>The Appraisal page: a journal of reflections, one year at a time.</summary>
public sealed partial class AppraisalsView : UserControl
{
    public AppraisalsView(AppraisalsViewModel viewModel, ShellViewModel shell)
    {
        ViewModel = viewModel;
        Shell = shell;
        InitializeComponent();
        Loaded += (_, _) => _ = ViewModel.RefreshAsync();
    }

    public AppraisalsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }
}
