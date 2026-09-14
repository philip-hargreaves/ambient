using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Ambient.App.Views;

// Sizes a document box inside a tab: at most a share of the tab's content
// area, which the star row of the tab's template gives once the tab is
// stretched. A host that scrolls the editor as a page bounds nothing
internal sealed class TabFit(TextBox box, double share)
{
    private ContentPresenter? _presenter;

    public void Fit(TabView tabs)
    {
        _presenter ??= Descendant<ContentPresenter>(tabs, "TabContentPresenter");
        if (_presenter is { ActualHeight: > 0 } presenter)
        {
            box.MaxHeight = Math.Max(box.MinHeight, presenter.ActualHeight * share);
        }
    }

    public void Follow() => box.MaxHeight = double.PositiveInfinity;

    private static T? Descendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.Name == name)
            {
                return match;
            }

            if (Descendant<T>(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
