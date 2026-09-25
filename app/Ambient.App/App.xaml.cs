using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Ambient.App.Composition;
using Ambient.App.Core.Hosting;
using Ambient.App.Shell;

namespace Ambient.App;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _window;
    private bool _closing;

    public App()
    {
        InitializeComponent();
        var paths = AppPaths.Default;
        _services = new ServiceCollection()
            .AddPlatform(paths)
            .AddEngine(paths)
            .AddViewModels(paths)
            .AddViews()
            .AddStartupTasks(paths)
            .BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startup = _services.GetRequiredService<StartupRunner>();
        startup.Run(StartupStage.BeforeWindow);
        _window = _services.GetRequiredService<MainWindow>();
        startup.Run(StartupStage.AfterWindow);
        _window.AppWindow.Closing += OnClosing;
        _window.Activate();
    }

    // The close is taken over: the shutdown asks first, then closes the window itself
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (!_closing)
        {
            e.Cancel = true;
            _ = CloseAsync();
        }
    }

    private async Task CloseAsync()
    {
        var shutdown = _services.GetRequiredService<AppShutdown>();
        if (_closing || !await shutdown.ConfirmAsync())
        {
            return;
        }

        _closing = true;
        await shutdown.StopAsync();
        _window?.Close();
        _services.Dispose();
    }
}
