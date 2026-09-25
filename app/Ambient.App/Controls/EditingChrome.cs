using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Ambient.App.Themes;

namespace Ambient.App.Controls;

/// <summary>The mode must be visible: an accent border while editing, the flat document look otherwise.</summary>
internal static class EditingChrome
{
    public static void Show(TextBox box, bool editing)
    {
        if (editing)
        {
            box.BorderBrush = ThemedResources.GetBrush("AccentFillColorDefaultBrush", box.ActualTheme);
            box.BorderThickness = new Thickness(1.5);
        }
        else
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
        }
    }
}
