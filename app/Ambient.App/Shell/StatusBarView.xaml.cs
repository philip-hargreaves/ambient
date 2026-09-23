using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
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

    private void BuildCredits()
    {
        CreditsRow.Children.Clear();
        var dark = ActualTheme == ElementTheme.Dark;
        foreach (var mark in _credits.Marks)
        {
            var path = dark ? mark.DarkPath : mark.LightPath;
            var uri = new Uri(path);
            CreditsRow.Children.Add(new Image
            {
                Height = mark.Height,
                Source = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    ? new SvgImageSource(uri)
                    : new BitmapImage(uri),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }
}
