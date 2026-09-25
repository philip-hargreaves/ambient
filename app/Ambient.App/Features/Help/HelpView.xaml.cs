using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Core.Shell;
using Ambient.App.Shell;

namespace Ambient.App.Features.Help;

/// <summary>The guide to using the app, and the small print about it.</summary>
public sealed partial class HelpView : UserControl
{
    private readonly CreditsViewModel _credits;

    public HelpView(CreditsViewModel credits)
    {
        _credits = credits;
        var version = typeof(HelpView).Assembly.GetName().Version ?? new Version(0, 0, 0);
        Version = $"Version {version.Major}.{version.Minor}.{version.Build}, for demonstration and evaluation";
        InitializeComponent();
        // Marks re-resolve on theme change, and on Loaded: a theme switched
        // while this view was off-tree fires no ActualThemeChanged here
        BuildMarks();
        ActualThemeChanged += (_, _) => BuildMarks();
        Loaded += (_, _) => BuildMarks();
    }

    public string Version { get; }

    private void BuildMarks() =>
        PartnerMarks.Fill(AboutMarks, _credits.Marks, ActualTheme == ElementTheme.Dark);
}
