using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ambient.App.Controls;

public sealed partial class DocumentTabs : UserControl
{
    private readonly List<UIElement> _views = [];

    public DocumentTabs() => InitializeComponent();

    /// <summary>Raised when another view is chosen.</summary>
    public event Action? SelectionChanged;

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

    /// <summary>Shows or hides a heading. A hidden view that was showing gives way to the first.</summary>
    public void SetVisible(int index, bool visible)
    {
        Bar.Items[index].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible && SelectedIndex == index)
        {
            SelectedIndex = 0;
        }
    }

    private void OnSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var chosen = SelectedIndex;
        for (var i = 0; i < _views.Count; i++)
        {
            _views[i].Visibility = i == chosen ? Visibility.Visible : Visibility.Collapsed;
        }
        SelectionChanged?.Invoke();
    }
}
