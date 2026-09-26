using Microsoft.UI.Xaml.Controls;
using ClinicAVT.App.Controls;
using ClinicAVT.App.Core.Features.Help;
using ClinicAVT.App.Core.Shell;

namespace ClinicAVT.App.Features.Help;

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
