using CommunityToolkit.Mvvm.ComponentModel;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Metrics;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Settings;

/// <summary>One installed corpus, as Settings lists it.</summary>
public sealed record CorpusRow(string Name, string Detail, string Attribution, bool Refused)
{
    /// <summary>A line above every row but the first.</summary>
    public bool Divided { get; init; }

    public bool AttributionVisible => Attribution.Length > 0;

    public bool Loaded => !Refused;
}

/// <summary>
/// The Settings page: four groups, each its own view model, and the engine events that
/// feed them. Every dependency is optional so the page works without an engine.
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

        client.ConnectedChanged += connected => Post(() =>
        {
            if (connected)
            {
                Connected();
            }
        });
        client.NotificationReceived += notification =>
        {
            switch (notification)
            {
                case NoteModelState model:
                    Post(() => NoteModel.Apply(model));
                    break;
                case GuidanceModelChanged or GuidanceDocumentChanged or GuidanceProgress:
                    Post(() => Guidance.Apply(notification));
                    break;
                default:
                    break;
            }
        };
        if (client.Connected)
        {
            Connected();
        }
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

    private void Post(Action action)
    {
        if (_dispatcher is null)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }
}
