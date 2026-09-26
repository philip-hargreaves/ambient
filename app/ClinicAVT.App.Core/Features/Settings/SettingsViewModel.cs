using CommunityToolkit.Mvvm.ComponentModel;
using ClinicAVT.App.Core.Common;
using ClinicAVT.App.Core.Features.Demo;
using ClinicAVT.App.Core.Metrics;
using ClinicAVT.App.Core.Ports;
using ClinicAVT.App.Core.Preferences;
using ClinicAVT.App.Core.Shell;
using ClinicAVT.Client;

namespace ClinicAVT.App.Core.Features.Settings;

/// <summary>
/// The Settings page. Each of its four groups has its own view model, and the engine events
/// that feed them arrive here. Every dependency is optional so the page works without an
/// engine.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUiDispatcher? _dispatcher;

    public SettingsViewModel(AppPreferences? preferences = null, IEngineHost? engine = null,
        ISessionState? session = null, StatusBarViewModel? status = null,
        IMachineInfoProvider? machine = null, PerformanceCollector? metrics = null,
        string? exportDirectory = null, IEngineApi? client = null,
        IUiDispatcher? dispatcher = null, DemoMode? demo = null, IDialogService? dialogs = null,
        IFilePicker? picker = null, ILauncher? launcher = null, IThemeService? theme = null)
    {
        _dispatcher = dispatcher;
        NoteModel = new NoteModelSettings(preferences, client, session, status);
        Guidance = new GuidanceLibrary(preferences, client, status, dialogs, picker, launcher);
        Privacy = new PrivacySettings(preferences, client, session, status, dialogs);
        Appearance = new AppearanceAndDiagnostics(
            preferences, engine, session, status, machine, metrics, exportDirectory, demo, picker, theme);

        if (client is null)
        {
            return;
        }

        client.NotificationReceived += notification => _dispatcher.PostOrRun(() =>
        {
            if (notification is NoteModelState model)
            {
                NoteModel.Apply(model);
            }
            else
            {
                Guidance.Apply(notification);
            }
        });
        client.OnConnected(_dispatcher, Connected);
    }

    public NoteModelSettings NoteModel { get; }

    public GuidanceLibrary Guidance { get; }

    public PrivacySettings Privacy { get; }

    public AppearanceAndDiagnostics Appearance { get; }

    [ObservableProperty]
    public partial string Heading { get; set; } = "Settings";

    private void Connected()
    {
        NoteModel.Connected();
        Guidance.Connected();
        Privacy.Connected();
    }
}
