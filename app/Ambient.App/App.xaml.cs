using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Ambient.App.Composition;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.App.Platform;
using Ambient.App.Shell;

namespace Ambient.App;

public partial class App : Application
{
    private Window? _window;
    private bool _closing;

    public App()
    {
        InitializeComponent();
        var paths = AppPaths.Default;
        Services = new ServiceCollection()
            .AddPlatform(paths)
            .AddEngine(paths)
            .AddViewModels(paths)
            .AddViews()
            .AddStartupTasks(paths)
            .BuildServiceProvider();
    }

    public new static App Current => (App)Application.Current;

    public ServiceProvider Services { get; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        RunStartupTasks(StartupStage.BeforeWindow);
        _window = Services.GetRequiredService<MainWindow>();
        Services.GetRequiredService<WindowAccessor>().Window = _window;
        RunStartupTasks(StartupStage.AfterWindow);
        _window.AppWindow.Closing += (sender, e) =>
        {
            if (!_closing)
            {
                e.Cancel = true;
                _ = ShutdownAsync();
            }
        };
        _window.Activate();
    }

    private void RunStartupTasks(StartupStage stage)
    {
        var logger = Services.GetRequiredService<ILogger<App>>();
        foreach (var task in Services.GetServices<IStartupTask>().Where(t => t.Stage == stage))
        {
            try
            {
                task.Run();
            }
            catch (Exception e)
            {
                logger.StartupTaskFailed(e, task.Name);
            }
        }
    }

    // In order: the review's edits saved and the engine told, the connection closed, the
    // engine stopped, then everything disposed. A recording is asked about first
    private async Task ShutdownAsync()
    {
        if (_closing)
        {
            return;
        }

        var session = Services.GetRequiredService<ConsultationViewModel>();
        if (session.ConsultationActive && session.State is SessionState.Recording or SessionState.Finalising
            && !await Services.GetRequiredService<IDialogService>().ConfirmAsync(
                "Close during a consultation?",
                "The recording so far is kept; the note will not be written.", "Close", "Keep recording"))
        {
            return;
        }

        _closing = true;
        var logger = Services.GetRequiredService<ILogger<App>>();
        try
        {
            await session.CloseReviewAsync();
            await Services.GetRequiredService<EngineConnection>().DisposeAsync();
            Services.GetRequiredService<IEngineHost>().Shutdown();
        }
        catch (Exception e)
        {
            logger.ShutdownStepFailed(e);
        }

        _window?.Close();
        Services.Dispose();
    }
}
