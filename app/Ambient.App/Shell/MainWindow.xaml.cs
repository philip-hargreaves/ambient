using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;

namespace Ambient.App.Shell;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;

    public StatusBarViewModel Status { get; }

    public MainWindow(NavigationService navigation, StatusBarViewModel status, StatusBarView statusBar)
    {
        _navigation = navigation;
        Status = status;
        InitializeComponent();
        StatusHost.Content = statusBar;

        navigation.Navigated += Select;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Unpackaged: the taskbar and title bar use the window icon
        AppWindow.SetIcon(System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        navigation.Attach(NavHost);
        navigation.NavigateTo("consultation");

        AppWindow.Resize(new SizeInt32(1280, 820));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 960;
            presenter.PreferredMinimumHeight = 640;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = args.IsSettingsSelected
            ? "settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (key is not null)
        {
            _navigation.NavigateTo(key);
        }
    }

    private void Select(string key)
    {
        if (key == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        foreach (var item in Nav.MenuItems)
        {
            if (item is NavigationViewItem entry && entry.Tag as string == key)
            {
                Nav.SelectedItem = entry;
                return;
            }
        }
    }
}
