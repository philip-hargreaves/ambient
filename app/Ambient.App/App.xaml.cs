using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage;
using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Features.Guidance;
using Ambient.App.Core.Features.Sessions;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Features.Appraisal;
using Ambient.App.Features.Consultation;
using Ambient.App.Features.Demo;
using Ambient.App.Features.Documents;
using Ambient.App.Features.Guidance;
using Ambient.App.Features.Sessions;
using Ambient.App.Features.Settings;
using Ambient.App.Platform;
using Ambient.App.Shell;
using Ambient.Client;

namespace Ambient.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    public new static App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    private static readonly TimeSpan EngineConnectTimeout = TimeSpan.FromSeconds(10);

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<WindowAccessor>();
        services.AddSingleton<IClipboard, WinUiClipboard>();
        services.AddSingleton<IFilePicker, WinUiFilePicker>();
        services.AddSingleton<ILauncher, WinUiLauncher>();
        services.AddSingleton<IThemeService, WinUiThemeService>();
        services.AddSingleton<IDialogService, WinUiDialogService>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEngineLauncher>(sp => new ProcessEngineLauncher(
            Path.Combine(AppContext.BaseDirectory, "ambient_engine.exe"),
            stderrPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ambient", "engine.log"),
            extraArguments: () =>
            {
                var prefs = sp.GetRequiredService<AppPreferences>();
                var args = new List<string>();
                if (prefs.NpuTranscription)
                {
                    args.Add("--asr-device NPU");
                }
#if DEBUG
                if (prefs.IncludeResearchGuidance)
                {
                    args.Add("--include-research");
                }
#endif
                return string.Join(" ", args);
            }));
        // Identity-free path: unpackaged runs have no ApplicationData
        var localState = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ambient");
        MigrateFromSotto(localState);
        services.AddSingleton<ICrashLog>(_ => new FileCrashLog(
            Path.Combine(localState, "crashes.jsonl")));
        CrashDumps.Register(
            Microsoft.Win32.Registry.CurrentUser, Path.Combine(localState, "dumps"),
            "ambient_engine.exe", "ambient_note_host.exe");
        services.AddSingleton<ISessionState>(sp => new DeferredSessionState(sp));
        services.AddSingleton<IEngineHost>(sp => new EngineSupervisor(
            sp.GetRequiredService<IEngineLauncher>(),
            sp.GetRequiredService<ISessionState>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ICrashLog>(),
            () => sp.GetRequiredService<EngineConnection>().MethodInFlight));
        services.AddSingleton(sp => new EngineConnection(
            sp.GetRequiredService<IEngineHost>(),
            static async (pid, ct) => await PipeTransport.ConnectAsync(
                EngineInfo.PipeName, EngineConnectTimeout, pid, ct).ConfigureAwait(false)));
        services.AddSingleton<IEngineTransport>(sp => sp.GetRequiredService<EngineConnection>());
        services.AddSingleton<IEngineApi>(sp => new EngineApi(sp.GetRequiredService<EngineConnection>()));

        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

        services.AddSingleton(_ => AppPreferences.Load(
            Path.Combine(localState, "preferences.json")));
        services.AddSingleton<Core.Metrics.IMachineInfoProvider,
            Core.Metrics.WmiMachineInfoProvider>();
        services.AddSingleton(sp => new Core.Metrics.PerformanceCollector(
            sp.GetRequiredService<IEngineApi>(),
            () => sp.GetRequiredService<AppPreferences>().CollectPerformanceData,
            () => sp.GetRequiredService<IEngineHost>().EnginePid,
            Path.Combine(localState, "metrics.jsonl")));
        services.AddSingleton<TranscriptViewModel>();
        services.AddSingleton<NoteViewModel>();
        services.AddSingleton<GuidanceViewModel>();
        services.AddSingleton<PageViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<MicViewModel>();
        services.AddSingleton(sp => new DemoMode(
            sp.GetRequiredService<AppPreferences>(), Path.Combine(localState, "masters.json")));
        services.AddSingleton<ConsultationViewModel>();
        services.AddSingleton<SessionControlsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<VoiceViewModel>();
        services.AddSingleton<SessionsViewModel>();
        services.AddSingleton<AppraisalsViewModel>();
        services.AddSingleton<DemoTrayViewModel>();
        services.AddSingleton<CreditsViewModel>();

        services.AddTransient<SessionControlsView>();
        services.AddTransient<DemoTrayView>();
        services.AddTransient<TranscriptPaneView>();
        services.AddTransient<GuidanceSectionView>();
        services.AddTransient<PageView>();
        services.AddTransient<NoteEditorView>();
        services.AddTransient<PatientEditorView>();
        services.AddTransient<NotePaneView>();
        services.AddTransient<StatusBarView>();
        services.AddTransient<ConsultationView>();
        services.AddTransient<SessionsView>();
        services.AddTransient<AppraisalsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    // One-time rename migration: sessions, preferences and the anchor move
    // from the sotto identity. Per item and never overwriting, so a stray
    // ambient folder (an engine run before the first app launch) cannot
    // block the real data from carrying over
    private static void MigrateFromSotto(string localState)
    {
        try
        {
            var old = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sotto");
            if (Directory.Exists(old))
            {
                var oldDb = Path.Combine(old, "store", "sotto.db");
                if (File.Exists(oldDb))
                {
                    foreach (var suffix in new[] { "", "-wal", "-shm" })
                    {
                        var source = oldDb + suffix;
                        if (File.Exists(source))
                        {
                            File.Move(source, Path.Combine(old, "store", "ambient.db" + suffix));
                        }
                    }
                }

                Merge(old, localState);
                if (!Directory.EnumerateFileSystemEntries(old).Any())
                {
                    Directory.Delete(old);
                }
            }

            foreach (var exe in new[] { "sotto_engine.exe", "sotto_note_host.exe" })
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\" + exe,
                    throwOnMissingSubKey: false);
            }
        }
        catch (Exception)
        {
            // A failed migration starts fresh; whatever remains stays for a retry
        }
    }

    private static void Merge(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var entry in Directory.EnumerateFileSystemEntries(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                if (Directory.Exists(dest))
                {
                    Merge(entry, dest);
                    if (!Directory.EnumerateFileSystemEntries(entry).Any())
                    {
                        Directory.Delete(entry);
                    }
                }
                else
                {
                    Directory.Move(entry, dest);
                }
            }
            else if (!File.Exists(dest))
            {
                File.Move(entry, dest);
            }
        }
    }

    // The client, the host and the session state form a cycle, so the view
    // model behind ISessionState is resolved on first read, not at build
    private sealed class DeferredSessionState(IServiceProvider services) : ISessionState
    {
        public bool ConsultationActive =>
            services.GetRequiredService<ConsultationViewModel>().ConsultationActive;

        public string SessionPhase =>
            services.GetRequiredService<ConsultationViewModel>().SessionPhase;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Subscribed here because a status-bar ctor dependency on the host
        // would close a DI cycle: host -> session state -> status bar
        var host = Services.GetRequiredService<IEngineHost>();
        var dispatcher = Services.GetRequiredService<IUiDispatcher>();
        var statusBar = Services.GetRequiredService<StatusBarViewModel>();
        // Applied here, not in the settings view model: the bar must obey the
        // saved preference before the settings page is ever opened
        statusBar.MetricsVisible =
            Services.GetRequiredService<AppPreferences>().ShowPerformanceMetrics;
        host.StatusChanged += _ =>
            dispatcher.Post(() => statusBar.SetEngineState(host.Status, host.Fault));

        _window = Services.GetRequiredService<MainWindow>();
        Services.GetRequiredService<WindowAccessor>().Window = _window;
        // Applied before Activate so a dark preference never flashes light
        Services.GetRequiredService<IThemeService>()
            .Apply(Services.GetRequiredService<AppPreferences>().Theme);
        _window.Closed += (_, _) => host.Shutdown();
        _window.Activate();
        host.Start();
        _ = RequestMicrophoneAccessAsync();
    }

    // Registers the app on the Settings microphone page; enforcement is the
    // engine's job
    private static async Task RequestMicrophoneAccessAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            return;
        }

        try
        {
            await Windows.Security.Authorization.AppCapabilityAccess.AppCapability
                .Create("microphone").RequestAccessAsync();
        }
        catch (Exception)
        {
            // The toggle stays wherever it was; the engine still honours it
        }
    }
}
