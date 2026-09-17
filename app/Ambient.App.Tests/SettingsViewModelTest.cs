using System.Text.Json;
using Ambient.App.Core;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.ViewModels;

namespace Ambient.App.Tests;

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

        var settings = new SettingsViewModel(preferences, engine, new FakeSession());
        settings.ConfirmKeepConsultations = () =>
        {
            asked++;
            return Task.FromResult(false);
        };

        Assert.True(settings.NpuTranscription);
        Assert.True(settings.KeepConsultations);
        Assert.Empty(engine.Calls);  // a launch must never restart the engine
        Assert.Equal(0, asked);
    }

    [Fact]
    public void SeedDataFollowsTheSwitchAndPersists()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineClient();
        var status = new StatusBarViewModel();
        var settings = new SettingsViewModel(preferences, status: status, client: engine);
        Assert.False(settings.SeedDataEnabled);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "demo/seed");

        settings.SeedDataEnabled = true;
        Assert.True(preferences.SeedDataEnabled);
        Assert.True(engine.SamplesSeeded);
        Assert.Contains("8 sample consultations added", status.LatestActivity);

        settings.SeedDataEnabled = false;
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
        var settings = new SettingsViewModel(preferences, status: status, client: engine);
        settings.SeedDataEnabled = true;
        var answer = false;
        settings.ConfirmDeleteAllConsultations = () => Task.FromResult(answer);

        await settings.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.True(settings.SeedDataEnabled);

        answer = true;
        await settings.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.Single(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.Contains("11 consultations deleted", status.LatestActivity);
        Assert.False(settings.SeedDataEnabled);
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
        var settings = new SettingsViewModel(TempPreferences(), status: status, client: engine)
        {
            ConfirmDeleteAllConsultations = () => Task.FromResult(true),
        };

        await settings.DeleteAllConsultationsCommand.ExecuteAsync(null);
        Assert.Single(engine.Requests, r => r.Method == "session/deleteAll");
        Assert.Contains("2 consultations deleted", status.LatestActivity);
        Assert.Single(settings.Documents);
    }

    [Fact]
    public async Task DeleteAllRefusesDuringAConsultation()
    {
        var engine = new FakeEngineClient { StoredSessions = 3 };
        var status = new StatusBarViewModel();
        var settings = new SettingsViewModel(TempPreferences(), session: new FakeSession { ConsultationActive = true },
            status: status, client: engine)
        {
            ConfirmDeleteAllConsultations = () => Task.FromResult(true),
        };

        await settings.DeleteAllConsultationsCommand.ExecuteAsync(null);

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

        var settings = new SettingsViewModel(preferences, status: status, client: engine);

        Assert.True(settings.SeedDataEnabled);
        Assert.Single(engine.Requests, r => r.Method == "demo/seed");
        Assert.DoesNotContain("sample", status.LatestActivity);  // nothing added: already there
    }

    [Fact]
    public void KeepConsultationsDefaultsOffAndPersists()
    {
        var preferences = TempPreferences();
        var settings = new SettingsViewModel(preferences);
        Assert.False(settings.KeepConsultations, "save nothing unless the clinician opts in");

        settings.KeepConsultations = true;  // no confirmer wired: acts directly
        Assert.True(preferences.KeepConsultations);

        var reopened = new SettingsViewModel(preferences);
        Assert.True(reopened.KeepConsultations);
    }

    [Fact]
    public async Task TurningOnIsConfirmedNeverJustToggled()
    {
        var preferences = TempPreferences();
        var settings = new SettingsViewModel(preferences);
        var asked = 0;
        var answer = false;
        settings.ConfirmKeepConsultations = () =>
        {
            asked++;
            return Task.FromResult(answer);
        };

        settings.KeepConsultations = true;
        await Task.Delay(20);
        Assert.Equal(1, asked);
        Assert.False(settings.KeepConsultations, "declined: the toggle stays off");
        Assert.False(preferences.KeepConsultations, "and nothing was persisted");

        answer = true;
        settings.KeepConsultations = true;
        await Task.Delay(20);
        Assert.Equal(2, asked);
        Assert.True(settings.KeepConsultations);
        Assert.True(preferences.KeepConsultations);

        settings.KeepConsultations = false;  // off is frictionless
        Assert.Equal(2, asked);
        Assert.False(preferences.KeepConsultations);
    }

    [Fact]
    public void NpuToggleSavesAndRestartsTheEngine()
    {
        var preferences = TempPreferences();
        var engine = new FakeEngineHost();
        var settings = new SettingsViewModel(preferences, engine, new FakeSession());

        settings.NpuTranscription = true;

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

        settings.NpuTranscription = true;

        Assert.False(settings.NpuTranscription);
        Assert.False(preferences.NpuTranscription);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void PreferencesSeedTheToggles()
    {
        var preferences = TempPreferences();
        preferences.NpuTranscription = true;
        preferences.DemoTrayEnabled = true;

        var settings = new SettingsViewModel(preferences);

        Assert.True(settings.NpuTranscription);
        Assert.True(settings.DemoTrayEnabled);
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

        var settings = new SettingsViewModel(preferences, client: engine);

        Assert.Equal(["Qwen3.5 4B", "Qwen3.5 9B", "Qwen3.6 35B"], settings.NoteModelOptions);
        Assert.Equal(2, settings.NoteModelIndex);
        Assert.True(settings.NoteModelEnabled);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/tier");  // restoring is not choosing
    }

    // A reconnect re-reads the store; the same models must not rebuild the bound
    // collection under the control, only a changed store does
    [Fact]
    public void AReconnectWithTheSameModelsLeavesTheCollectionAlone()
    {
        var engine = TieredEngine();
        var settings = new SettingsViewModel(TempPreferences(), client: engine, session: new FakeSession());
        var changes = 0;
        settings.NoteModelOptions.CollectionChanged += (_, _) => changes++;
        settings.NoteModelIndex = 2;

        engine.SetConnected(false);
        engine.SetConnected(true);

        Assert.Equal(0, changes);
        Assert.Equal(2, settings.NoteModelIndex);

        engine.ExtraNoteModels.RemoveAt(1);  // the 4B was uninstalled
        engine.SetConnected(false);
        engine.SetConnected(true);

        Assert.True(changes > 0);
        Assert.Equal(["Qwen3.5 9B", "Qwen3.6 35B"], settings.NoteModelOptions);
        Assert.Equal(1, settings.NoteModelIndex);
    }

    [Fact]
    public void ASingleStagedModelLeavesNothingToChoose()
    {
        var settings = new SettingsViewModel(TempPreferences(), client: new FakeEngineClient());

        Assert.Equal(["Qwen3.5 9B"], settings.NoteModelOptions);
        Assert.Equal(0, settings.NoteModelIndex);
        Assert.False(settings.NoteModelEnabled);
        Assert.Equal("Only one model installed", settings.NoteModelStatus);
    }

    [Fact]
    public void APreferenceForAnUninstalledTierFallsBackToTheDefault()
    {
        var preferences = TempPreferences();
        preferences.NoteTier = "accuracy";

        var settings = new SettingsViewModel(preferences, client: new FakeEngineClient());

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(0, settings.NoteModelIndex);
        Assert.Contains("not installed", settings.NoteModelStatus);
    }

    [Fact]
    public void ChoosingATierPersistsItConfiguresTheEngineAndGreysUntilReady()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: engine, session: new FakeSession());

        settings.NoteModelIndex = 2;

        Assert.Equal("accuracy", preferences.NoteTier);
        var request = engine.Requests.Single(r => r.Method == "note/tier");
        Assert.Contains("accuracy", request.Params);
        Assert.False(settings.NoteModelEnabled, "greyed while the lane loads");
        Assert.Equal("Loading", settings.NoteModelStatus);

        engine.RaiseNotification("note/model", NoteModel("loading", "accuracy", "Qwen3.6 35B"));
        Assert.False(settings.NoteModelEnabled);

        engine.RaiseNotification("note/model", NoteModel("ready", "accuracy", "Qwen3.6 35B"));
        Assert.True(settings.NoteModelEnabled);
        Assert.Equal("", settings.NoteModelStatus);
        Assert.StartsWith("Larger models", settings.NoteModelCaption);
        Assert.Equal(2, settings.NoteModelIndex);
    }

    [Fact]
    public void AFailedLoadRevertsToTheTierThatWorked()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: engine, session: new FakeSession());
        settings.NoteModelIndex = 2;
        engine.Requests.Clear();

        engine.RaiseNotification(
            "note/model", NoteModel("failed", "accuracy", "Qwen3.6 35B", "out of memory"));

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(1, settings.NoteModelIndex);
        Assert.Contains("out of memory", settings.NoteModelStatus);
        var back = engine.Requests.Single(r => r.Method == "note/tier");
        Assert.Contains("default", back.Params);
    }

    [Fact]
    public void ARefusedRequestRevertsToo()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: engine, session: new FakeSession());
        engine.FailNext = method => method == "note/tier" ? new IOException("no such device") : null;

        settings.NoteModelIndex = 0;

        Assert.Equal("default", preferences.NoteTier);
        Assert.Equal(1, settings.NoteModelIndex);
        Assert.Contains("no such device", settings.NoteModelStatus);
    }

    [Fact]
    public void ChoosingATierRefusesDuringAConsultation()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var session = new FakeSession { ConsultationActive = true };
        var settings = new SettingsViewModel(preferences, client: engine, session: session);

        settings.NoteModelIndex = 2;

        Assert.Equal(1, settings.NoteModelIndex);
        Assert.Equal("default", preferences.NoteTier);
        Assert.DoesNotContain(engine.Requests, r => r.Method == "note/tier");
    }

    [Fact]
    public void TheEngineIsAuthoritativeAboutWhatIsResident()
    {
        var preferences = TempPreferences();
        var engine = TieredEngine();
        var settings = new SettingsViewModel(preferences, client: engine, session: new FakeSession());

        // Another shell instance, or the engine's own default: the control follows
        engine.RaiseNotification("note/model", NoteModel("ready", "constrained", "Qwen3.5 4B"));

        Assert.Equal(0, settings.NoteModelIndex);
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
            new FakeEngineClient(), () => true, () => null, Path.Combine(dir, "metrics.jsonl"));
        collector.SessionStarted("mic", 0, null);
        collector.StopRequested();
        await collector.SessionFinishedAsync(null, 10);
        var settings = new SettingsViewModel(
            machine: new FixedMachine(), metrics: collector, exportDirectory: dir);

        settings.ExportPerformanceReportCommand.Execute(null);

        Assert.StartsWith("saved ", settings.ExportResult);
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
            new FakeEngineClient(), () => true, () => null, Path.Combine(dir, "metrics.jsonl"));
        collector.SessionStarted("mic", 0, null);
        collector.StopRequested();
        await collector.SessionFinishedAsync(null, 10);
        var settings = new SettingsViewModel(machine: new FixedMachine(), metrics: collector);
        var chosen = Path.Combine(dir, "picked.html");

        settings.PickSavePath = _ => Task.FromResult<string?>(null);
        await settings.ExportPerformanceReportCommand.ExecuteAsync(null);
        Assert.Equal("", settings.ExportResult);
        Assert.False(File.Exists(chosen));

        settings.PickSavePath = _ => Task.FromResult<string?>(chosen);
        await settings.ExportPerformanceReportCommand.ExecuteAsync(null);
        Assert.True(File.Exists(chosen), "written where the picker chose");
        Assert.Contains("picked.html", settings.ExportResult);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ExportWithoutDataExplainsItself()
    {
        var settings = new SettingsViewModel(machine: new FixedMachine());
        settings.ExportPerformanceReportCommand.Execute(null);
        Assert.Equal("no performance data collected yet", settings.ExportResult);
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
        var applied = new List<string>();
        var settings = new SettingsViewModel(preferences);
        settings.ApplyTheme = applied.Add;
        Assert.Equal("system", settings.Theme);
        Assert.Equal(0, settings.ThemeIndex);

        settings.ThemeIndex = 2;

        Assert.Equal("dark", settings.Theme);
        Assert.Equal(["dark"], applied);
        Assert.Equal("dark", preferences.Theme);

        var reopened = new SettingsViewModel(preferences);
        Assert.Equal(2, reopened.ThemeIndex);
    }

    [Fact]
    public void RestoringASavedThemeFiresNoHandlers()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var preferences = new AppPreferences(path) { Theme = "light" };

        var settings = new SettingsViewModel(preferences);

        Assert.Equal("light", settings.Theme);
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
        Assert.False(settings.ShowPerformanceMetrics, "chips are for testing, not GPs");
        Assert.False(bar.MetricsVisible);

        settings.ShowPerformanceMetrics = true;

        Assert.True(bar.MetricsVisible);
        Assert.True(preferences.ShowPerformanceMetrics);
    }

    [Fact]
    public void InstalledCorporaListWhatTheEngineHas()
    {
        var engine = new FakeEngineClient();
        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        var corpus = Assert.Single(settings.GuidanceCorpora);
        Assert.Equal("Fixture guidance corpus", corpus.Name);
        Assert.Equal("40 passages · 11 Sep 2026", corpus.Detail);
        Assert.Equal("none", corpus.Attribution);
        Assert.False(corpus.Refused);
        Assert.Equal("", settings.GuidanceCaption);
        Assert.False(settings.GuidanceCaptionVisible);
        Assert.False(corpus.Divided);
    }

    [Fact]
    public void TheInstalledGuidanceCardIsHiddenWhenNothingIsInstalled()
    {
        var withNice = new FakeEngineClient { GuidanceState = "ready" };
        withNice.GuidanceCorpora.Add(new { name = "NICE guidance", chunks = 22991 });
        Assert.True(new SettingsViewModel(TempPreferences(), client: withNice)
            .GuidanceInstalledVisible);

        var empty = new FakeEngineClient { GuidanceState = "ready" };
        empty.GuidanceCorpora.Clear();
        var settings = new SettingsViewModel(TempPreferences(), client: empty);
        Assert.Empty(settings.GuidanceCorpora);
        Assert.Equal("", settings.GuidanceCaption);
        Assert.False(settings.GuidanceInstalledVisible);
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

        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        Assert.Empty(settings.GuidanceCorpora);
        Assert.Equal(
            "Unavailable: guidance embedder gte-large-int8: tokenizer ignores max_length",
            settings.GuidanceCaption);
        Assert.True(settings.GuidanceCaptionVisible);
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

        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        var corpus = Assert.Single(settings.GuidanceCorpora);
        Assert.Equal("nice-2026-08", corpus.Name);
        Assert.Equal("Not used: corpus.db sha256 does not match the manifest", corpus.Detail);
        Assert.True(corpus.Refused);
        Assert.False(corpus.Loaded);
        Assert.Equal("", settings.GuidanceCaption);
    }

    [Fact]
    public void TheListFollowsTheEngineWhenTheGuidanceModelArrives()
    {
        var engine = new FakeEngineClient { GuidanceState = "loading" };
        engine.GuidanceCorpora.Clear();
        var settings = new SettingsViewModel(TempPreferences(), client: engine);
        Assert.Equal("Loading", settings.GuidanceCaption);

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

        var corpus = Assert.Single(settings.GuidanceCorpora);
        Assert.Equal("22,991 passages · 11 Sep 2026", corpus.Detail);
        Assert.Equal("Contains public sector information", corpus.Attribution);
        Assert.Equal("", settings.GuidanceCaption);
    }

    private static object Document(long id, string name, string state, int chunks = 0,
        string? error = null, int pages = 0, int pagesWithoutText = 0, string? path = null) => new
    {
        id,
        name,
        path = path ?? name + ".txt",
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
    public void AddedDocumentsListWorkingRowsFirstThenByName()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        engine.GuidanceDocuments.Add(Document(2, "asthma", "ready", 12));
        engine.GuidanceDocuments.Add(Document(3, "PMR", "indexing"));
        engine.GuidanceDocuments.Add(Document(4, "Letter", "failed", error: "patientData"));
        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        Assert.Equal(["PMR", "asthma", "Gout", "Letter"], settings.Documents.Select(d => d.Name));
        Assert.Equal("Waiting", settings.Documents[0].Detail);
        Assert.True(settings.Documents[0].Waiting);
        Assert.Equal("12 passages · added 15 Sep 2026", settings.Documents[1].Detail);
        Assert.StartsWith("Not searched: this looks like a document about a patient.",
            settings.Documents[3].Detail);
        Assert.True(settings.Documents[3].Failed);
        Assert.Equal("Reading 1 document", settings.BatchCaption);
        Assert.True(settings.DocumentsPresent);
        Assert.True(settings.AddDocumentsCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddingDocumentsSendsThePathsAndCountsWhatWasSkipped()
    {
        var engine = new FakeEngineClient();
        engine.AddedDocuments.Add(Document(5, "PMR pathway", "indexing"));
        engine.SkippedDocuments.Add(new { path = @"C:\g\scan.pdf", reason = "unsupported" });
        engine.SkippedDocuments.Add(new { path = @"C:\g\empty.txt", reason = "unreadable" });
        var settings = new SettingsViewModel(TempPreferences(), client: engine)
        {
            PickDocuments = () => Task.FromResult<IReadOnlyList<string>>(
                [@"C:\g\PMR pathway.txt", @"C:\g\scan.pdf", @"C:\g\empty.txt"]),
        };

        await settings.AddDocumentsCommand.ExecuteAsync(null);

        var request = Assert.Single(engine.Requests, r => r.Method == "guidance/documents/add");
        Assert.Contains("PMR pathway.txt", request.Params);
        var row = Assert.Single(settings.Documents);
        Assert.Equal("PMR pathway", row.Name);
        Assert.True(row.Working);
        Assert.Equal("1 skipped, not PDF or text · 1 could not be read", settings.DocumentsCaption);
    }

    [Fact]
    public void RowsSayWhyAPdfFailedAndHowLongAReadyOneIs()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(1, "Scan", "failed", error: "noText", pages: 40,
            pagesWithoutText: 38));
        engine.GuidanceDocuments.Add(Document(2, "Locked", "failed", error: "password"));
        engine.GuidanceDocuments.Add(Document(3, "PMR", "ready", 310, pages: 41));
        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        Assert.Equal(["Locked", "PMR", "Scan"], settings.Documents.Select(d => d.Name));
        Assert.Equal("Cannot be read: the PDF is password protected.", settings.Documents[0].Detail);
        Assert.Equal("41 pages · 310 passages · added 15 Sep 2026", settings.Documents[1].Detail);
        Assert.Equal(
            "Cannot be searched: 38 of 40 pages are images with no text.",
            settings.Documents[2].Detail);
    }

    [Fact]
    public async Task RowsFollowTheEngineAndRemoveAlwaysConfirmsBecauseItBinsTheFile()
    {
        var engine = new FakeEngineClient();
        engine.GuidanceDocuments.Add(Document(3, "PMR", "indexing"));
        var asked = 0;
        var settings = new SettingsViewModel(TempPreferences(), client: engine)
        {
            ConfirmRemoveDocument = _ =>
            {
                asked++;
                return Task.FromResult(true);
            },
        };
        var row = Assert.Single(settings.Documents);

        engine.RaiseNotification("guidance/progress",
            Json(new { id = 3, phase = "paused", done = 0, total = 10 }));
        Assert.Equal("Waiting for the consultation to finish", row.Detail);
        engine.RaiseNotification("guidance/progress",
            Json(new { id = 3, phase = "preparing", done = 3, total = 10 }));
        Assert.Equal("Preparing 3 of 10 passages", row.Detail);
        Assert.Equal(0.3, row.Progress, 3);
        Assert.False(row.Waiting);

        // Removing a document that is still being read asks first, since it bins the file
        await settings.RemoveDocumentCommand.ExecuteAsync(row);
        Assert.Equal(1, asked);
        Assert.Contains(engine.Requests,
            r => r.Method == "guidance/documents/remove" && r.Params == "{\"id\":3}");

        engine.RaiseNotification("guidance/document", Json(Document(3, "PMR", "ready", 10)));
        Assert.Equal("10 passages · added 15 Sep 2026", row.Detail);
        Assert.False(row.Working);
        Assert.Equal("", settings.BatchCaption);

        await settings.RemoveDocumentCommand.ExecuteAsync(row);
        Assert.Equal(2, asked);

        engine.RaiseNotification("guidance/document", Json(Document(3, "PMR", "removed", 10)));
        Assert.Empty(settings.Documents);
        Assert.False(settings.DocumentsPresent);
    }

    [Fact]
    public void DeveloperToolsStayClosedUntilOpenedAndThenStayOpen()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var settings = new SettingsViewModel(new AppPreferences(path));
        Assert.False(settings.DeveloperToolsExpanded);

        settings.ToggleDeveloperToolsCommand.Execute(null);
        Assert.True(settings.DeveloperToolsExpanded);
        Assert.False(settings.DeveloperToolsCollapsed);

        var saved = AppPreferences.Load(path);
        Assert.True(saved.DeveloperToolsExpanded);
        Assert.True(new SettingsViewModel(saved).DeveloperToolsExpanded);
    }

    [Fact]
    public void TheFolderIsListedAndAddableEvenBeforeTheGuidanceModelLoads()
    {
        var engine = new FakeEngineClient { GuidanceState = "loading" };
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        // Copying a file into the folder needs no embedder, so Add is always available
        Assert.True(settings.AddDocumentsCommand.CanExecute(null));
        Assert.Single(settings.Documents);
        Assert.Equal(@"C:\Users\clinician\Documents\Ambient guidelines", settings.GuidelinesFolder);
        Assert.False(settings.FolderMissing);
    }

    [Fact]
    public void AnUnreachableFolderIsFlaggedAndNothingIsRemoved()
    {
        var engine = new FakeEngineClient { GuidelinesFolderFound = false, UnsupportedFiles = 2 };
        engine.GuidanceDocuments.Add(Document(1, "Gout", "ready", 41));
        var settings = new SettingsViewModel(TempPreferences(), client: engine);

        Assert.True(settings.FolderMissing);
        Assert.Single(settings.Documents);
        Assert.Equal("2 other files are not searched, not PDF or text", settings.DocumentsCaption);
    }
}
