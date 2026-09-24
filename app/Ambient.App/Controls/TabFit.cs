using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ambient.App.Controls;

// Sizes a document box inside a tabbed area: at most a share of the area's
// height. A host that scrolls the editor as a page bounds nothing
internal sealed class TabFit(TextBox box, double share)
{
    public void Fit(FrameworkElement area)
    {
        if (area.ActualHeight > 0)
        {
            box.MaxHeight = Math.Max(box.MinHeight, area.ActualHeight * share);
        }
    }

    public void Follow() => box.MaxHeight = double.PositiveInfinity;
}
