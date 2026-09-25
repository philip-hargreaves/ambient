using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Ambient.App.Core.Shell;

namespace Ambient.App.Shell;

/// <summary>The partner marks as a row of images, in the theme's variant of each.</summary>
internal static class PartnerMarks
{
    public static void Fill(Panel row, IReadOnlyList<CreditMark> marks, bool dark)
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
