using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;

namespace Ambient.App.Composition;

/// <summary>Launch, as an ordered list: the data first, then the engine once the window shows.</summary>
internal static class StartupTasks
{
    public static IServiceCollection AddStartupTasks(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton<IStartupTask>(sp => new MigrateSottoData(paths, sp.GetRequiredService<ILogger<MigrateSottoData>>()));
        services.AddSingleton<IStartupTask>(_ => new RegisterCrashDumps(paths));
        services.AddSingleton<IStartupTask, AttachSessionState>();
        services.AddSingleton<IStartupTask, ApplySavedPreferences>();
        services.AddSingleton<IStartupTask, ApplyTheme>();
        services.AddSingleton<IStartupTask, StartEngine>();
        services.AddSingleton<IStartupTask, RequestMicrophoneAccess>();
        return services;
    }

    // Runs before the preferences and the store are read; a failure logs and the app starts fresh
    private sealed class MigrateSottoData(AppPaths paths, ILogger logger) : IStartupTask
    {
        public string Name => "migrate sotto data";

        public StartupStage Stage => StartupStage.BeforeWindow;

        public void Run()
        {
            if (SottoMigration.Run(paths.OldLocalState, paths.LocalState))
            {
                logger.SottoDataMoved(paths.LocalState);
            }

            foreach (var exe in new[] { "sotto_engine.exe", "sotto_note_host.exe" })
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\" + exe,
                    throwOnMissingSubKey: false);
            }
        }
    }

    // The engine host reads the session through LiveSessionState, which follows the view model
    private sealed class AttachSessionState(LiveSessionState state, ConsultationViewModel session) : IStartupTask
    {
        public string Name => "attach session state";

        public StartupStage Stage => StartupStage.BeforeWindow;

        public void Run() => state.Follow(session);
    }

    private sealed class RegisterCrashDumps(AppPaths paths) : IStartupTask
    {
        public string Name => "register crash dumps";

        public StartupStage Stage => StartupStage.BeforeWindow;

        public void Run() => CrashDumps.Register(
            Microsoft.Win32.Registry.CurrentUser, paths.Dumps,
            EngineLayout.EngineExe, EngineLayout.NoteHostExe);
    }

    // The bar obeys the saved preference before the settings page is ever opened
    private sealed class ApplySavedPreferences(AppPreferences preferences, StatusBarViewModel status) : IStartupTask
    {
        public string Name => "apply saved preferences";

        public StartupStage Stage => StartupStage.BeforeWindow;

        public void Run() => status.MetricsVisible = preferences.ShowPerformanceMetrics;
    }

    // Before Activate, so a dark preference never flashes light
    private sealed class ApplyTheme(AppPreferences preferences, IThemeService theme) : IStartupTask
    {
        public string Name => "apply theme";

        public StartupStage Stage => StartupStage.AfterWindow;

        public void Run() => theme.Apply(preferences.Theme);
    }

    private sealed class StartEngine(IEngineHost host, IUiDispatcher dispatcher, StatusBarViewModel status) : IStartupTask
    {
        public string Name => "start engine";

        public StartupStage Stage => StartupStage.AfterWindow;

        public void Run()
        {
            host.StatusChanged += _ => dispatcher.Post(() => status.SetEngineState(host.Status, host.Fault));
            host.Start();
        }
    }

    // Registers the app on the Settings microphone page; enforcement is the engine's job
    private sealed class RequestMicrophoneAccess(ILogger<RequestMicrophoneAccess> logger) : IStartupTask
    {
        public string Name => "request microphone access";

        public StartupStage Stage => StartupStage.AfterWindow;

        public void Run()
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            {
                _ = RequestAsync();
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.18362")]
        private async Task RequestAsync()
        {
            try
            {
                await Windows.Security.Authorization.AppCapabilityAccess.AppCapability
                    .Create("microphone").RequestAccessAsync();
            }
            catch (Exception e)
            {
                // The toggle stays wherever it was; the engine still honours it
                logger.StepFailed("microphone access request", e.Message);
            }
        }
    }
}
