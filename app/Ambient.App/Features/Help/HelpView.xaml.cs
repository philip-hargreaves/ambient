using Microsoft.UI.Xaml.Controls;
using Ambient.App.Controls;
using Ambient.App.Core.Features.Help;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Help;

/// <summary>The guide to using the app, and the small print about it.</summary>
public sealed partial class HelpView : UserControl
{
    public HelpView(HelpViewModel viewModel, CreditsViewModel credits)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PartnerMarks.Attach(this, AboutMarks, credits.Marks);
    }

    public HelpViewModel ViewModel { get; }
}
