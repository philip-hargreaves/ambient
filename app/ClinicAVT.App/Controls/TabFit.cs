using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClinicAVT.App.Controls;

/// <summary>Caps a document box at a share of its tabbed area's height.</summary>
internal sealed class TabFit(TextBox box, double share)
{
    public void Fit(FrameworkElement area)
    {
        if (area.ActualHeight > 0)
        {
            box.MaxHeight = Math.Max(box.MinHeight, area.ActualHeight * share);
        }
    }

    /// <summary>Caps the box at the area's height less the chrome the editor stacks around it.</summary>
    public void FitWithin(FrameworkElement area, double chrome)
    {
        if (area.ActualHeight > 0)
        {
            box.MaxHeight = Math.Max(box.MinHeight, area.ActualHeight - chrome);
        }
    }
}
