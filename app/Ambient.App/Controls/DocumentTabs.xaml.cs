using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ambient.App.Controls;

public sealed partial class DocumentTabs : UserControl
{
    private readonly List<UIElement> _views = [];
    private bool _reasserting;

    public DocumentTabs() => InitializeComponent();

    /// <summary>The area a view fills, for sizing an editor to it.</summary>
    public FrameworkElement ContentArea => Host;

    public int SelectedIndex
    {
        get => Bar.SelectedItem is { } item ? Bar.Items.IndexOf(item) : -1;
        set => Bar.SelectedItem = Bar.Items[value];
    }

    /// <summary>Adds a view under a heading. The first one added is shown.</summary>
    public void Add(string heading, UIElement view)
    {
        Bar.Items.Add(new SelectorBarItem { Text = heading });
        _views.Add(view);
        view.Visibility = Visibility.Collapsed;
        Host.Children.Add(view);
        if (_views.Count == 1)
        {
            SelectedIndex = 0;
        }
    }

    /// <summary>Shows or hides a heading. A hidden view that was showing gives way to the first shown one.</summary>
    public void SetVisible(int index, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (Bar.Items[index].Visibility == visibility)
        {
            return;
        }

        Bar.Items[index].Visibility = visibility;
        if (!visible && SelectedIndex == index)
        {
            for (var i = 0; i < Bar.Items.Count; i++)
            {
                if (Bar.Items[i].Visibility == Visibility.Visible)
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }

        // The bar draws its selection from the items' positions, which just moved: set it
        // again once laid out, whatever it is by then, without the views reacting to the
        // moment in between
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Bar.SelectedItem is not { } current)
            {
                return;
            }

            _reasserting = true;
            Bar.SelectedItem = null;
            Bar.SelectedItem = current;
            _reasserting = false;
        });
    }

    private void OnSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_reasserting)
        {
            return;
        }

        var chosen = SelectedIndex;
        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].Visibility = i == chosen ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
