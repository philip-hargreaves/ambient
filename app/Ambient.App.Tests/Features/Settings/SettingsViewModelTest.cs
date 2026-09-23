using System.Text.Json;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;
using Ambient.Client;
using static Ambient.App.Tests.Support.Waits;

namespace Ambient.App.Tests.Features.Settings;

public class SettingsViewModelTest
{
    private sealed class FakeEngineHost : IEngineHost
    {
        public event Action<EngineStatus>? StatusChanged;

        public EngineStatus Status { get; private set; }

        public EngineFault? Fault => null;

        public int? EnginePid => null;

        public List<string> Calls { get; } = [];

        public void Start()
        {
            Calls.Add("start");
            Status = EngineStatus.Running;
            StatusChanged?.Invoke(Status);
        }

        public void Shutdown()
        {
            Calls.Add("shutdown");
            Status = EngineStatus.Stopped;
            StatusChanged?.Invoke(Status);
        }
    }

    private sealed class FakeSession : ISessionState
    {
        public bool ConsultationActive { get; set; }
    }

    private static AppPreferences TempPreferences() =>
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

    [Fact]
    public void RestoringSavedSettingsFiresNoHandlers()
    {
        var preferences = TempPreferences();
        preferences.NpuTranscription = true;
        preferences.KeepConsultations = true;
        var engine = new FakeEngineHost();
        var asked = 0;

        var dialogs = new FakeDialogService { Answer = false, OnConfirm = () => asked++ };
        var settings = new SettingsViewModel(preferences, engine, new FakeSession(), dialogs: dialogs);

        Assert.True(settings.Appearance.NpuTranscription);
        Assert.True(settings.Privacy.KeepConsultations);
        Assert.Empty(engine.Calls);  // a launch must never restart the engine
        Assert.Equal(0, asked);
    }

    [Fact]
    public void SeedDataFollowsTheSwitchAndPersists()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineClient();
        var status = new StatusBarViewModel();
        var settings = new SettingsViewModel(preferences, status: status, client: new EngineApi(engine));
        Assert.False(settings.Privacy.SeedDataEnabled);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "demo/seed");

        settings.Privacy.SeedDataEnabled = true;
        Assert.True(preferences.SeedDataEnabled);
        Assert.True(engine.SamplesSeeded);
        Assert.Contains("8 sample consultations added", status.LatestActivity);

        settings.Privacy.SeedDataEnabled = false;
        Assert.False(preferences.SeedDataEnabled);
        Assert.False(engine.SamplesSeeded);
        Assert.Contains("8 sample consultations removed", status.LatestActivity);
    }

    [Fact]
    public async Task DeleteAllAsksFirstThenErasesAndTurnsSeedDataOff()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineClient { StoredSessions = 3 };
        var status = new StatusBarViewModel();
        var dialogs = new FakeDialogService { Answer = false };
        var settings = new SettingsViewModel(preferences, status: status, client: new EngineApi(engine), dialogs: dialogs);
        settings.Privacy.SeedDataEnabled = true;

        await settings.Privacy.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.True(settings.Privacy.SeedDataEnabled);

        dialogs.Answer = true;
        await settings.Privacy.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.Single(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.Contains("11 consultations deleted", status.LatestActivity);
        Assert.False(settings.Privacy.SeedDataEnabled);
        Assert.False(preferences.SeedDataEnabled);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "demo/clear");  // nothing left to clear
        Assert.Equal(0, engine.StoredSessions);
    }

    [Fact]
    public async Task DeleteAllLeavesTheGuidelineDocumentsAlone()
    {
        var engine = new FakeEngineClient { StoredSessions = 2 };
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var status = new StatusBarViewModel();
        var settings = new SettingsViewModel(TempPreferences(), status: status, client: new EngineApi(engine),
            dialogs: new FakeDialogService());

        await settings.Privacy.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.Single(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.Contains("2 consultations deleted", status.LatestActivity);
        Assert.Single(settings.Guidance.Documents);
    }

    [Fact]
    public async Task DeleteAllRefusesDuringAConsultation()
    {
        var engine = new FakeEngineClient { StoredSessions = 3 };
        var status = new StatusBarViewModel();
        var settings = new SettingsViewModel(TempPreferences(), session: new FakeSession { ConsultationActive = true },
            status: status, client: new EngineApi(engine), dialogs: new FakeDialogService());

        await settings.Privacy.DeleteAllConsultationsCommand.ExecuteAsync(null);

        Assert.DoesNotContain(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.Contains("finish the consultation", status.LatestActivity);
    }

    [Fact]
    public void ALaunchWithSeedDataOnSeedsOnceAndQuietly()
    {
        var preferences = TempPreferences();
        preferences.SeedDataEnabled = true;
        var engine = new FakeEngineClient { SamplesSeeded = true };
        var status = new StatusBarViewModel();

        var settings = new SettingsViewModel(preferences, status: status, client: new EngineApi(engine));

        Assert.True(settings.Privacy.SeedDataEnabled);
        Assert.Single(engine.Requests, r => r.Method == "demo/seed");
        Assert.DoesNotContain("sample", status.LatestActivity);  // nothing added: already there
    }

    [Fact]
    public void KeepConsultationsDefaultsOffAndPersists()
    {
        var preferences = TempPreferences();
        var settings = new SettingsViewModel(preferences);
        Assert.False(settings.Privacy.KeepConsultations, "save nothing unless the clinician opts in");

        settings.Privacy.KeepConsultations = true;  // no confirmer wired: acts directly
        Assert.True(preferences.KeepConsultations);

        var reopened = new SettingsViewModel(preferences);
        Assert.True(reopened.Privacy.KeepConsultations);
    }

    [Fact]
    public async Task TurningOnIsConfirmedNeverJustToggled()
    {
        var preferences = TempPreferences();
        var asked = 0;
        var dialogs = new FakeDialogService { Answer = false, OnConfirm = () => asked++ };
        var settings = new SettingsViewModel(preferences, dialogs: dialogs);

        settings.Privacy.KeepConsultations = true;
        await WaitUntilAsync(() => asked == 1);
        Assert.Equal(1, asked);
        Assert.False(settings.Privacy.KeepConsultations, "declined: the toggle stays off");
        Assert.False(preferences.KeepConsultations, "and nothing was persisted");

        dialogs.Answer = true;
        settings.Privacy.KeepConsultations = true;
        await WaitUntilAsync(() => asked == 2);
        Assert.Equal(2, asked);
        Assert.True(settings.Privacy.KeepConsultations);
        Assert.True(preferences.KeepConsultations);

        settings.Privacy.KeepConsultations = false;  // off is frictionless
        Assert.Equal(2, asked);
        Assert.False(preferences.KeepConsultations);
    }

    [Fact]
    public void NpuToggleSavesAndRestartsTheEngine()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineHost();
        var settings = new SettingsViewModel(preferences, engine, new FakeSession());

        settings.Appearance.NpuTranscription = true;

        Assert.True(preferences.NpuTranscription);
        Assert.Equal(["shutdown", "start"], engine.Calls);
    }

    [Fact]
    public void NpuToggleRefusesDuringAConsultation()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineHost();
        var session = new FakeSession { ConsultationActive = true };
        var settings = new SettingsViewModel(preferences, engine, session);

        settings.Appearance.NpuTranscription = true;

        Assert.False(settings.Appearance.NpuTranscription);
        Assert.False(preferences.NpuTranscription);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void TheDocumentsListIsClosedUnlessLeftOpenAndAFailureOpensItOnce()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var preferences = TempPreferences();
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine));

        Assert.Equal("1 document · all ready", settings.Guidance.DocumentsSummary);
        Assert.False(settings.Guidance.DocumentsExpanded);

        engine.RaiseNotification("guidance/document", Json(Document(2, "Letter", "failed", error: "password")));
        Assert.True(settings.Guidance.DocumentsExpanded);
        Assert.False(preferences.DocumentsExpanded, "opening for attention is not the remembered choice");

        settings.Guidance.ToggleDocumentsCommand.Execute(null);
        Assert.False(settings.Guidance.DocumentsExpanded);
        engine.RaiseNotification("guidance/document", Json(Document(3, "PMR", "ready", 10)));
        Assert.False(settings.Guidance.DocumentsExpanded, "the same failure does not reopen it");

        settings.Guidance.ToggleDocumentsCommand.Execute(null);
        Assert.True(preferences.DocumentsExpanded);
        Assert.True(new SettingsViewModel(preferences).Guidance.DocumentsExpanded);
    }

    [Fact]
    public void DemoModeRowFollowsTheSavedRuns()
    {
        var masters = Path.Combine(Path.GetTempPath(), $"ambient-masters-{Guid.NewGuid():N}.json");
        File.WriteAllText(masters, """{"Elbow swelling":{"id":"s-elbow","audioSeconds":540},"Chest pain":{"id":"s-chest","audioSeconds":457}}""");
        var preferences = TempPreferences();
        var demo = new Ambient.App.Core.Features.Demo.DemoMode(preferences, masters, []);
        var settings = new SettingsViewModel(preferences, demo: demo);

        Assert.True(settings.Appearance.DemoTracksAvailable);
        Assert.False(settings.Appearance.DemoModeEnabled);
        settings.Appearance.DemoModeEnabled = true;
        settings.Appearance.DemoTrackIndex = 1;
        Assert.True(demo.Enabled);
        Assert.Equal("s-chest", demo.Master!.SessionId);
        Assert.True(preferences.DemoMode);
        Assert.Equal("Chest pain", preferences.DemoTrack);

        var none = new SettingsViewModel(preferences, demo: new Ambient.App.Core.Features.Demo.DemoMode(
            preferences, Path.Combine(Path.GetTempPath(), "missing.json"), []));
        Assert.False(none.Appearance.DemoTracksAvailable);
        Assert.Contains("record_masters", none.Appearance.DemoModeCaption);
    }

    [Fact]
    public void PreferencesSeedTheToggles()
    {
        var preferences = TempPreferences();
        preferences.NpuTranscription = true;
        preferences.DemoTrayEnabled = true;

        var settings = new SettingsViewModel(preferences);

        Assert.True(settings.Appearance.NpuTranscription);
        Assert.True(settings.Appearance.DemoTrayEnabled);
    }

    // ---- note model tier ---------------------------------------------------

    private static FakeEngineClient TieredEngine()
    {
        var engine = new FakeEngineClient();
        engine.ExtraNoteModels.Add(("qwen3.6-35b-a3b-int4", "Qwen3.6 35B", "accuracy"));
        engine.ExtraNoteModels.Add(("qwen3.5-4b-int4", "Qwen3.5 4B", "constrained"));
        return engine;
    }

    private static System.Text.Json.JsonElement NoteModel(
        string state, string tier, string name, string? detail = null) =>
        System.Text.Json.JsonSerializer.SerializeToElement(
            new { state, tier, name, id = tier, seconds = 12.0, firstUse = false, detail });

    [Fact]
    public void NoteModelsComeFromTheEngineInLadderOrderAndThePreferenceSelects()
    {
        var preferences = TempPreferences();
        preferences.NoteTier = "accuracy";
        var engine = TieredEngine();

        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine));

        Assert.Equal(["Qwen3.5 4B", "Qwen3.5 9B", "Qwen3.6 35B"], settings.NoteModel.NoteModelOptions);
        Assert.Equal(2, settings.NoteModel.NoteModelIndex);
        Assert.True(settings.NoteModel.NoteModelEnabled);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/tier");  // restoring is not choosing
    }

    // A reconnect re-reads the store; the same models must not rebuild the bound
    // collection under the control, only a changed store does
    [Fact]
    public void AReconnectWithTheSameModelsLeavesTheCollectionAlone()
    {
        var engine = TieredEngine();
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine), session: new FakeSession());
        var changes = 0;
        settings.NoteModel.NoteModelOptions.CollectionChanged += (_, _) => changes++;
        settings.NoteModel.NoteModelIndex = 2;

        engine.SetConnected(false);
        engine.SetConnected(true);

        Assert.Equal(0, changes);
        Assert.Equal(2, settings.NoteModel.NoteModelIndex);

        engine.ExtraNoteModels.RemoveAt(1);  // the 4B was uninstalled
        engine.SetConnected(false);
        engine.SetConnected(true);

        Assert.True(changes > 0);
        Assert.Equal(["Qwen3.5 9B", "Qwen3.6 35B"], settings.NoteModel.NoteModelOptions);
        Assert.Equal(1, settings.NoteModel.NoteModelIndex);
    }

    [Fact]
    public void ASingleStagedModelLeavesNothingToChoose()
    {
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(new FakeEngineClient()));

        Assert.Equal(["Qwen3.5 9B"], settings.NoteModel.NoteModelOptions);
        Assert.Equal(0, settings.NoteModel.NoteModelIndex);
        Assert.False(settings.NoteModel.NoteModelEnabled);
        Assert.Equal("Only one model installed", settings.NoteModel.NoteModelStatus);
    }

    [Fact]
    public void APreferenceForAnUninstalledTierFallsBackToTheDefault()
    {
        var preferences = TempPreferences();
        preferences.NoteTier = "accuracy";

        var settings = new SettingsViewModel(preferences, client: new EngineApi(new FakeEngineClient()));

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(0, settings.NoteModel.NoteModelIndex);
        Assert.Contains("not installed", settings.NoteModel.NoteModelStatus);
    }

    [Fact]
    public void ChoosingATierPersistsItConfiguresTheEngineAndGreysUntilReady()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine), session: new FakeSession());

        settings.NoteModel.NoteModelIndex = 2;

        Assert.Equal("accuracy", preferences.NoteTier);
        var request = engine.Requests.Single(r => r.Method == "note/tier");
        Assert.Contains("accuracy", request.Params);
        Assert.False(settings.NoteModel.NoteModelEnabled, "greyed while the lane loads");
        Assert.Equal("Loading", settings.NoteModel.NoteModelStatus);

        engine.RaiseNotification("note/model", NoteModel("loading", "accuracy", "Qwen3.6 35B"));
        Assert.False(settings.NoteModel.NoteModelEnabled);

        engine.RaiseNotification("note/model", NoteModel("ready", "accuracy", "Qwen3.6 35B"));
        Assert.True(settings.NoteModel.NoteModelEnabled);
        Assert.Equal("", settings.NoteModel.NoteModelStatus);
        Assert.StartsWith("Larger models", settings.NoteModel.NoteModelCaption);
        Assert.Equal(2, settings.NoteModel.NoteModelIndex);
    }

    [Fact]
    public void AFailedLoadRevertsToTheTierThatWorked()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine), session: new FakeSession());
        settings.NoteModel.NoteModelIndex = 2;
        engine.Requests.Clear();

        engine.RaiseNotification(
            "note/model", NoteModel("failed", "accuracy", "Qwen3.6 35B", "out of memory"));

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(1, settings.NoteModel.NoteModelIndex);
        Assert.Contains("out of memory", settings.NoteModel.NoteModelStatus);
        var back = engine.Requests.Single(r => r.Method == "note/tier");
        Assert.Contains("default", back.Params);
    }

    [Fact]
    public void ARefusedRequestRevertsToo()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine), session: new FakeSession());
        engine.FailNext = method => method == "note/tier" ? new IOException("no such device") : null;

        settings.NoteModel.NoteModelIndex = 0;

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(1, settings.NoteModel.NoteModelIndex);
        Assert.Contains("no such device", settings.NoteModel.NoteModelStatus);
    }

    [Fact]
    public void ChoosingATierRefusesDuringAConsultation()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var session = new FakeSession { ConsultationActive = true };
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine), session: session);

        settings.NoteModel.NoteModelIndex = 2;

        Assert.Equal(1, settings.NoteModel.NoteModelIndex);
        Assert.Equal("default", preferences.NoteTier);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/tier");
    }

    [Fact]
    public void TheEngineIsAuthoritativeAboutWhatIsResident()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: new EngineApi(engine), session: new FakeSession());

        // Another shell instance, or the engine's own default: the control follows
        engine.RaiseNotification("note/model", NoteModel("ready", "constrained", "Qwen3.5 4B"));

        Assert.Equal(0, settings.NoteModel.NoteModelIndex);
        Assert.Equal("constrained", preferences.NoteTier);
    }

    private sealed class FixedMachine : Ambient.App.Core.Metrics.IMachineInfoProvider
    {
        public Ambient.App.Core.Metrics.MachineInfo Describe() =>
            new("TestCpu", 32, "TestOs", [], null);
    }

    [Fact]
    public async Task ExportWritesTheHtmlReport()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var collector = new Ambient.App.Core.Metrics.PerformanceCollector(
            new EngineApi(new FakeEngineClient()), () => true, () => null, Path.Combine(dir, "metrics.jsonl"));
        collector.SessionStarted("mic", 0, null);
        collector.StopRequested();
        await collector.SessionFinishedAsync(null, 10);
        var settings = new SettingsViewModel(
            machine: new FixedMachine(), metrics: collector, exportDirectory: dir);

        settings.Appearance.ExportPerformanceReportCommand.Execute(null);

        Assert.StartsWith("saved ", settings.Appearance.ExportResult);
        var report = Directory.GetFiles(dir, "ambient-perf-*.html").Single();
        Assert.Contains("TestCpu", File.ReadAllText(report));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task ExportGoesWhereThePickerChoseAndCancelIsSilent()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var collector = new Ambient.App.Core.Metrics.PerformanceCollector(
            new EngineApi(new FakeEngineClient()), () => true, () => null, Path.Combine(dir, "metrics.jsonl"));
        collector.SessionStarted("mic", 0, null);
        collector.StopRequested();
        await collector.SessionFinishedAsync(null, 10);
        var picker = new FakeFilePicker();
        var settings = new SettingsViewModel(machine: new FixedMachine(), metrics: collector, picker: picker);
        var chosen = Path.Combine(dir, "picked.html");

        await settings.Appearance.ExportPerformanceReportCommand.ExecuteAsync(null);
        Assert.Equal("", settings.Appearance.ExportResult);
        Assert.False(File.Exists(chosen));

        picker.SavePath = chosen;
        await settings.Appearance.ExportPerformanceReportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(chosen), "written where the picker chose");
        Assert.Contains("picked.html", settings.Appearance.ExportResult);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ExportWithoutDataExplainsItself()
    {
        var settings = new SettingsViewModel(machine: new FixedMachine());
        settings.Appearance.ExportPerformanceReportCommand.Execute(null);
        Assert.Equal("no performance data collected yet", settings.Appearance.ExportResult);
    }

    [Fact]
    public void PreferencesRoundTripThroughTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var preferences = new AppPreferences(path)
        {
            NpuTranscription = true,
            ShowPerformanceMetrics = true,
        };
        preferences.Save();

        var loaded = AppPreferences.Load(path);
        Assert.True(loaded.NpuTranscription);
        Assert.True(loaded.ShowPerformanceMetrics);
        File.Delete(path);
    }

    [Fact]
    public void ThemeDefaultsToSystemPersistsAndAppliesLive()
    {
        var preferences = TempPreferences();
        var theme = new FakeThemeService();
        var settings = new SettingsViewModel(preferences, theme: theme);
        Assert.Equal("system", settings.Appearance.Theme);
        Assert.Equal(0, settings.Appearance.ThemeIndex);

        settings.Appearance.ThemeIndex = 2;

        Assert.Equal("dark", settings.Appearance.Theme);
        Assert.Equal(["dark"], theme.Applied);
        Assert.Equal("dark", preferences.Theme);

        var reopened = new SettingsViewModel(preferences);
        Assert.Equal(2, reopened.Appearance.ThemeIndex);
    }

    [Fact]
    public void RestoringASavedThemeFiresNoHandlers()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var preferences = new AppPreferences(path) { Theme = "light" };

        var settings = new SettingsViewModel(preferences);

        Assert.Equal("light", settings.Appearance.Theme);
        Assert.False(File.Exists(path), "launch restore must not re-save");
    }

    [Fact]
    public void AnUnknownStoredThemeFallsToSystem()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var preferences = new AppPreferences(path) { Theme = "dark" };
        preferences.Save();
        File.WriteAllText(path, File.ReadAllText(path).Replace("dark", "solarized"));

        Assert.Equal("system", AppPreferences.Load(path).Theme);
        File.Delete(path);
    }

    [Fact]
    public void MetricsToggleDefaultsOffAndReachesTheBar()
    {
        var preferences = TempPreferences();
        var bar = new StatusBarViewModel();
        var settings = new SettingsViewModel(preferences, status: bar);
        Assert.False(settings.Appearance.ShowPerformanceMetrics, "chips are for testing, not GPs");
        Assert.False(bar.MetricsVisible);

        settings.Appearance.ShowPerformanceMetrics = true;

        Assert.True(bar.MetricsVisible);
        Assert.True(preferences.ShowPerformanceMetrics);
    }

    [Fact]
    public void InstalledCorporaListWhatTheEngineHas()
    {
        var engine = new FakeEngineClient();
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        var corpus = Assert.Single(settings.Guidance.GuidanceCorpora);
        Assert.Equal("Fixture guidance corpus", corpus.Name);
        Assert.Equal("40 passages · 11 Sep 2026", corpus.Detail);
        Assert.Equal("none", corpus.Attribution);
        Assert.False(corpus.Refused);
        Assert.Equal("", settings.Guidance.GuidanceCaption);
        Assert.False(settings.Guidance.GuidanceCaptionVisible);
        Assert.False(corpus.Divided);
    }

    [Fact]
    public void TheInstalledGuidanceCardIsHiddenWhenNothingIsInstalled()
    {
        var withNice = new FakeEngineClient { GuidanceState = "ready" };
        withNice.GuidanceCorpora.Add(new { name = "NICE guidance", chunks = 22991 });
        Assert.True(new SettingsViewModel(TempPreferences(), client: new EngineApi(withNice))
            .Guidance.GuidanceInstalledVisible);

        var empty = new FakeEngineClient { GuidanceState = "ready" };
        empty.GuidanceCorpora.Clear();
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(empty));
        Assert.Empty(settings.Guidance.GuidanceCorpora);
        Assert.Equal("", settings.Guidance.GuidanceCaption);
        Assert.False(settings.Guidance.GuidanceInstalledVisible);
    }

    [Fact]
    public void AnUnavailableGuidanceModelSaysWhyAndListsNothing()
    {
        var engine = new FakeEngineClient
        {
            GuidanceState = "unavailable",
            GuidanceDetail = "guidance embedder gte-large-int8: tokenizer ignores max_length",
        };
        engine.GuidanceCorpora.Clear();

        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        Assert.Empty(settings.Guidance.GuidanceCorpora);
        Assert.Equal(
            "Unavailable: guidance embedder gte-large-int8: tokenizer ignores max_length",
            settings.Guidance.GuidanceCaption);
        Assert.True(settings.Guidance.GuidanceCaptionVisible);
    }

    [Fact]
    public void ARefusedCorpusKeepsItsPlaceWithTheReason()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceCorpora.Clear();
        engine.GuidanceCorpora.Add(new
        {
            id = "nice-2026-08",
            unavailable = "corpus.db sha256 does not match the manifest",
        });

        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        var corpus = Assert.Single(settings.Guidance.GuidanceCorpora);
        Assert.Equal("nice-2026-08", corpus.Name);
        Assert.Equal("Not used: corpus.db sha256 does not match the manifest", corpus.Detail);
        Assert.True(corpus.Refused);
        Assert.False(corpus.Loaded);
        Assert.Equal("", settings.Guidance.GuidanceCaption);
    }

    [Fact]
    public void TheListFollowsTheEngineWhenTheGuidanceModelArrives()
    {
        var engine = new FakeEngineClient { GuidanceState = "loading" };
        engine.GuidanceCorpora.Clear();
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));
        Assert.Equal("Loading", settings.Guidance.GuidanceCaption);

        engine.GuidanceState = "ready";
        engine.GuidanceCorpora.Add(new
        {
            name = "NICE guidance",
            licence = "OGL v3",
            attribution = "Contains public sector information",
            chunks = 22991,
            builtAt = "2026-09-11T21:03:17Z",
        });
        engine.RaiseNotification("guidance/model");

        var corpus = Assert.Single(settings.Guidance.GuidanceCorpora);
        Assert.Equal("22,991 passages · 11 Sep 2026", corpus.Detail);
        Assert.Equal("Contains public sector information", corpus.Attribution);
        Assert.Equal("", settings.Guidance.GuidanceCaption);
    }

    private static object Document(long id, string name, string state, int chunks = 0,
        string? error = null, int pages = 0, int pagesWithoutText = 0) => new
        {
            id,
            name,
            path = name + ".txt",
            sha256 = new string('0', 64),
            mime = "text/plain",
            state,
            error,
            addedAt = "2026-09-15T09:12:44Z",
            indexedAt = state == "ready" ? "2026-09-15T09:13:02Z" : null,
            bytes = 1000,
            pages,
            pagesWithoutText,
            chunks,
        };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void AddedDocumentsListWorkingRowsFirstThenUnreadableThenByName()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        engine.GuidanceDocuments.Add(Document(2, "asthma", "ready", 12));
        engine.GuidanceDocuments.Add(Document(3, "PMR", "indexing"));
        engine.GuidanceDocuments.Add(Document(4, "Letter", "failed", error: "patientData"));
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        Assert.Equal(["PMR", "Letter", "asthma", "Gout"], settings.Guidance.Documents.Select(d => d.Name));
        Assert.Equal("Waiting", settings.Guidance.Documents[0].Detail);
        Assert.True(settings.Guidance.Documents[0].Waiting);
        Assert.StartsWith("Not searched: this looks like a document about a patient.",
            settings.Guidance.Documents[1].Detail);
        Assert.True(settings.Guidance.Documents[1].Failed);
        Assert.Equal("12 passages · added 15 Sep 2026", settings.Guidance.Documents[2].Detail);
        Assert.Equal("4 documents · reading 1, 1 could not be read", settings.Guidance.DocumentsSummary);
        Assert.True(settings.Guidance.DocumentsExpanded, "work or a failure opens the list");
        Assert.True(settings.Guidance.DocumentsPresent);
        Assert.True(settings.Guidance.AddDocumentsCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddingDocumentsSendsThePathsAndCountsWhatWasSkipped()
    {
        var engine = new FakeEngineClient();
        engine.AddedDocuments.Add(Document(5, "PMR pathway", "indexing"));
        engine.SkippedDocuments.Add(new { path = @"C:\g\scan.pdf", reason = "unsupported" });
        engine.SkippedDocuments.Add(new { path = @"C:\g\empty.txt", reason = "unreadable" });
        var picker = new FakeFilePicker
        {
            Files = [@"C:\g\PMR pathway.txt", @"C:\g\scan.pdf", @"C:\g\empty.txt"],
        };
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine), picker: picker);

        await settings.Guidance.AddDocumentsCommand.ExecuteAsync(null);

        var request = Assert.Single(engine.Requests, r => r.Method == "guidance/documents/add");
        Assert.Contains("PMR pathway.txt", request.Params);
        var row = Assert.Single(settings.Guidance.Documents);
        Assert.Equal("PMR pathway", row.Name);
        Assert.True(row.Working);
        Assert.Equal("1 skipped, not PDF or text · 1 could not be read", settings.Guidance.DocumentsCaption);
    }

    [Fact]
    public void RowsSayWhyAPdfFailedAndHowLongAReadyOneIs()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(1, "Scan", "failed", error: "noText", pages: 40,
            pagesWithoutText: 38));
        engine.GuidanceDocuments.Add(Document(2, "Locked", "failed", error: "password"));
        engine.GuidanceDocuments.Add(Document(3, "PMR", "ready", 310, pages: 41));
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        Assert.Equal(["Locked", "Scan", "PMR"], settings.Guidance.Documents.Select(d => d.Name));
        Assert.Equal("Cannot be read: the PDF is password protected.", settings.Guidance.Documents[0].Detail);
        Assert.Equal(
            "Cannot be searched: 38 of 40 pages are images with no text.",
            settings.Guidance.Documents[1].Detail);
        Assert.Equal("41 pages · 310 passages · added 15 Sep 2026", settings.Guidance.Documents[2].Detail);
    }

    [Fact]
    public async Task RowsFollowTheEngineAndRemoveAlwaysConfirmsBecauseItBinsTheFile()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(3, "PMR", "indexing"));
        var asked = 0;
        var dialogs = new FakeDialogService { OnConfirm = () => asked++ };
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine), dialogs: dialogs);
        var row = Assert.Single(settings.Guidance.Documents);

        engine.RaiseNotification("guidance/progress",
            Json(new { id = 3, phase = "paused", done = 0, total = 10 }));
        Assert.Equal("Waiting for the consultation to finish", row.Detail);
        engine.RaiseNotification("guidance/progress",
            Json(new { id = 3, phase = "preparing", done = 3, total = 10 }));
        Assert.Equal("Preparing 3 of 10 passages", row.Detail);
        Assert.Equal(0.3, row.Progress, 3);
        Assert.False(row.Waiting);

        // Removing a document that is still being read asks first, since it bins the file
        await settings.Guidance.RemoveDocumentCommand.ExecuteAsync(row);
        Assert.Equal(1, asked);
        Assert.Contains(engine.Requests,
            r => r.Method == "guidance/documents/remove" && r.Params == "{\"id\":3}");

        engine.RaiseNotification("guidance/document", Json(Document(3, "PMR", "ready", 10)));
        Assert.Equal("10 passages · added 15 Sep 2026", row.Detail);
        Assert.False(row.Working);
        Assert.EndsWith("all ready", settings.Guidance.DocumentsSummary);

        await settings.Guidance.RemoveDocumentCommand.ExecuteAsync(row);
        Assert.Equal(2, asked);

        engine.RaiseNotification("guidance/document", Json(Document(3, "PMR", "removed", 10)));
        Assert.Empty(settings.Guidance.Documents);
        Assert.False(settings.Guidance.DocumentsPresent);
    }

    [Fact]
    public void DeveloperToolsStayClosedUntilOpenedAndThenStayOpen()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var settings = new SettingsViewModel(new AppPreferences(path));
        Assert.False(settings.Appearance.DeveloperToolsExpanded);

        settings.Appearance.ToggleDeveloperToolsCommand.Execute(null);
        Assert.True(settings.Appearance.DeveloperToolsExpanded);
        Assert.False(settings.Appearance.DeveloperToolsCollapsed);

        var saved = AppPreferences.Load(path);
        Assert.True(saved.DeveloperToolsExpanded);
        Assert.True(new SettingsViewModel(saved).Appearance.DeveloperToolsExpanded);
    }

    [Fact]
    public void TheFolderIsListedAndAddableEvenBeforeTheGuidanceModelLoads()
    {
        var engine = new FakeEngineClient { GuidanceState = "loading" };
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        // Copying a file into the folder needs no embedder, so Add is always available
        Assert.True(settings.Guidance.AddDocumentsCommand.CanExecute(null));
        Assert.Single(settings.Guidance.Documents);
        Assert.Equal(@"C:\Users\clinician\Documents\Ambient guidelines", settings.Guidance.GuidelinesFolder);
        Assert.False(settings.Guidance.FolderMissing);
    }

    [Theory]
    [InlineData(@"C:\Users\p\OneDrive\Documents\Ambient guidelines", true)]
    [InlineData(@"C:\Users\p\onedrive - ucl\Documents\Ambient guidelines", true)]
    [InlineData(@"C:\Users\p\Documents\Ambient guidelines", false)]
    [InlineData("", false)]
    public void OneDriveFolderIsRecognised(string folder, bool expected)
    {
        var roots = new[] { @"C:\Users\p\OneDrive", @"C:\Users\p\OneDrive - UCL" };
        Assert.Equal(expected, GuidanceLibrary.InOneDrive(folder, roots));
    }

    [Fact]
    public void AnUnreachableFolderIsFlaggedAndNothingIsRemoved()
    {
        var engine = new FakeEngineClient { GuidelinesFolderFound = false, UnsupportedFiles = 2 };
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var settings = new SettingsViewModel(TempPreferences(), client: new EngineApi(engine));

        Assert.True(settings.Guidance.FolderMissing);
        Assert.Single(settings.Guidance.Documents);
        Assert.Equal("2 other files are not searched, not PDF or text", settings.Guidance.DocumentsCaption);
    }
}
