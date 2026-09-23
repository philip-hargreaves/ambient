using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ambient.App.Controls;

/// <summary>The mode must be visible: an accent border while editing, the flat document look otherwise.</summary>
internal static class EditingChrome
{
    public static void Show(TextBox box, bool editing)
    {
        if (editing)
        {
            box.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["AccentFillColorDefaultBrush"];
            box.BorderThickness = new Thickness(1.5);
        }
        else
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
        }
    }
}
