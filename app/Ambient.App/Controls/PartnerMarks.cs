using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Ambient.App.Core.Shell;

namespace Ambient.App.Controls;

/// <summary>The partner marks as a row of images, in the theme's variant of each.</summary>
internal static class PartnerMarks
{
    // Rebuilt on theme change and on Loaded: a theme switched while the view was
    // off-tree raises no ActualThemeChanged on it
    public static void Attach(FrameworkElement view, Panel row, IReadOnlyList<CreditMark> marks)
    {
        void Build() => Fill(row, marks, view.ActualTheme == ElementTheme.Dark);
        Build();
        view.ActualThemeChanged += (_, _) => Build();
        view.Loaded += (_, _) => Build();
    }

    private static void Fill(Panel row, IReadOnlyList<CreditMark> marks, bool dark)
    {
        row.Children.Clear();
        foreach (var mark in marks)
        {
            var path = dark ? mark.DarkPath : mark.LightPath;
            var uri = new Uri(path);
            row.Children.Add(new Image
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
