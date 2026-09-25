using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;

namespace Ambient.App.Shell;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;
    private readonly SessionsViewModel _sessions;

    public StatusBarViewModel Status { get; }

    public MainWindow(
        NavigationService navigation, StatusBarViewModel status, StatusBarView statusBar,
        SessionsViewModel sessions)
    {
        _navigation = navigation;
        _sessions = sessions;
        Status = status;
        InitializeComponent();
        StatusHost.Content = statusBar;

        navigation.Navigated += Select;
        // The pane opens itself at a wide width once its template applies, so the closed start
        // the markup asks for is restated after load
        Nav.Loaded += (_, _) => Nav.IsPaneOpen = false;

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

    private async void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var key = args.IsSettingsSelected
            ? "settings"
            : (args.SelectedItem as NavigationViewItem)?.Tag as string;
        if (key is null)
        {
            return;
        }
        // Consultation means record a new one: a stored review ends before the page shows
        if (key == "consultation")
        {
            await _sessions.CloseStoredReviewAsync();
        }
        _navigation.NavigateTo(key);
    }

    private void Select(string key)
    {
        if (key == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        foreach (var item in Nav.MenuItems.Concat(Nav.FooterMenuItems))
        {
            if (item is NavigationViewItem entry && entry.Tag as string == key)
            {
                Nav.SelectedItem = entry;
                return;
            }
        }
    }
}
