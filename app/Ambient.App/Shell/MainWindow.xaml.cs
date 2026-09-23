using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;

namespace Ambient.App.Shell;

public sealed partial class MainWindow : Window
{
    public StatusBarViewModel Status { get; }

    public MainWindow(NavigationService navigation, StatusBarViewModel status)
    {
        Status = status;
        InitializeComponent();

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
}
