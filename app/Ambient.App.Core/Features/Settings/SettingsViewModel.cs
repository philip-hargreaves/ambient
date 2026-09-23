using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Features.Demo;
using Ambient.App.Core.Features.Guidance;
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

public sealed partial class SettingsViewModel : ObservableObject
{

    private readonly AppPreferences? _preferences;
    private readonly IEngineHost? _engine;
    private readonly ISessionState? _session;
    private readonly StatusBarViewModel? _status;
    private readonly IMachineInfoProvider? _machine;
    private readonly PerformanceCollector? _metrics;
    private readonly DemoMode? _demo;
    private readonly IEngineApi? _client;
    private readonly IUiDispatcher? _dispatcher;
    private readonly IDialogService? _dialogs;
    private readonly IFilePicker? _picker;
    private readonly ILauncher? _launcher;
    private readonly IThemeService? _theme;
    private readonly string _exportDirectory;
    private bool _reverting;

    public SettingsViewModel(AppPreferences? preferences = null, IEngineHost? engine = null,
        ISessionState? session = null, StatusBarViewModel? status = null,
        IMachineInfoProvider? machine = null, PerformanceCollector? metrics = null,
        string? exportDirectory = null, IEngineApi? client = null,
        IUiDispatcher? dispatcher = null, DemoMode? demo = null, IDialogService? dialogs = null,
        IFilePicker? picker = null, ILauncher? launcher = null, IThemeService? theme = null)
    {
        _preferences = preferences;
        _dialogs = dialogs;
        _picker = picker;
        _launcher = launcher;
        _theme = theme;
        _demo = demo;
        _engine = engine;
        _session = session;
        _status = status;
        _machine = machine;
        _metrics = metrics;
        _client = client;
        _dispatcher = dispatcher;
        _exportDirectory = exportDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        // Restoring saved values is not the clinician changing them: the
        // handlers (persist, confirm, engine restart) must not fire here
        _initialising = true;
        DemoTrayEnabled = preferences?.DemoTrayEnabled ?? false;
        DemoModeEnabled = demo?.Enabled ?? false;
        SeedDataEnabled = preferences?.SeedDataEnabled ?? false;
        NpuTranscription = preferences?.NpuTranscription ?? false;
        CollectPerformanceData = preferences?.CollectPerformanceData ?? false;
        KeepConsultations = preferences?.KeepConsultations ?? false;
        ShowPerformanceMetrics = preferences?.ShowPerformanceMetrics ?? false;
        DeveloperToolsExpanded = preferences?.DeveloperToolsExpanded ?? false;
        DocumentsExpanded = preferences?.DocumentsExpanded ?? false;
        IncludeResearchGuidance = preferences?.IncludeResearchGuidance ?? false;
        Theme = preferences?.Theme ?? "system";
        _noteTier = preferences?.NoteTier ?? "default";
        _initialising = false;

        if (client is not null)
        {
            // Options come from the engine's store; the lane's state drives the control
            client.ConnectedChanged += connected => Post(() =>
            {
                if (connected)
                {
                    _ = LoadNoteModelsAsync();
                    _ = LoadGuidanceCorporaAsync();
                    _ = LoadDocumentsAsync();
                    if (SeedDataEnabled)
                    {
                        _ = ApplySeedDataAsync(true);
                    }
                }
            });
            client.NotificationReceived += (method, parameters) =>
            {
                if (method == "note/model")
                {
                    var snapshot = parameters.Clone();
                    Post(() => OnNoteModel(snapshot));
                }
                else if (method == "guidance/model")
                {
                    Post(() =>
                    {
                        _ = LoadGuidanceCorporaAsync();
                        _ = LoadDocumentsAsync();
                    });
                }
                else if (method == "guidance/document"
                    && Protocol.Parse<DocumentInfo>(parameters) is { } document)
                {
                    Post(() => Upsert(document));
                }
                else if (method == "guidance/progress")
                {
                    var progress = parameters.Clone();
                    Post(() => ApplyProgress(progress));
                }
            };
            if (client.Connected)
            {
                _ = LoadNoteModelsAsync();
                _ = LoadGuidanceCorporaAsync();
                _ = LoadDocumentsAsync();
                if (SeedDataEnabled)
                {
                    _ = ApplySeedDataAsync(true);
                }
            }
        }
    }

    private readonly bool _initialising;

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

    // ---- note model tier --------------------------------------------------

    // Tier keys in ladder order, parallel to NoteModelOptions
    private readonly List<string> _tiers = [];
    private string _noteTier;
    private string? _revertTier;  // where a failed switch goes back to
    private bool _populating;

    /// <summary>Display names of the staged note models, smallest first.</summary>
    public ObservableCollection<string> NoteModelOptions { get; } = [];

    /// <summary>The chosen note model as the control's selection.</summary>
    [ObservableProperty]
    public partial int NoteModelIndex { get; set; } = -1;

    /// <summary>False while the lane loads, and when there is nothing to choose between.</summary>
    [ObservableProperty]
    public partial bool NoteModelEnabled { get; set; }

    /// <summary>Lane status; empty when nothing is happening.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteModelCaption))]
    public partial string NoteModelStatus { get; set; } = "";

    /// <summary>The caption under the control: the status while there is one, else the description.</summary>
    public string NoteModelCaption =>
        string.IsNullOrEmpty(NoteModelStatus) ? "Larger models are more accurate and use more memory." : NoteModelStatus;

    /// <summary>The tier the shell wants; the engine's store resolves it.</summary>
    public string NoteTier => _noteTier;

    private static int LadderRank(string tier) => tier switch
    {
        "constrained" => 0,
        "default" => 1,
        "accuracy" => 2,
        _ => 3,
    };

    private async Task LoadNoteModelsAsync()
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            var models = await _client.ListModelsAsync().ConfigureAwait(true);
            var staged = models
                .Where(m => m.Task == "note")
                .Select(m => (
                    m.Tier,
                    Name: string.IsNullOrWhiteSpace(m.Name)
                        ? StatusBarViewModel.FriendlyModelName(m.Id)
                        : m.Name))
                .Where(m => AppPreferences.NoteTiers.Contains(m.Tier))
                .OrderBy(m => LadderRank(m.Tier))
                .ToList();

            _populating = true;
            // Rebuilt only when the store's contents changed: clearing ComboBox items
            // under an open popup or a live selection can fault in XAML, and every
            // reconnect would otherwise do it
            var tiers = staged.Select(m => m.Tier).ToList();
            var names = staged.Select(m => m.Name).ToList();
            if (!tiers.SequenceEqual(_tiers) || !names.SequenceEqual(NoteModelOptions))
            {
                NoteModelIndex = -1;  // clear the selection before the items
                _tiers.Clear();
                NoteModelOptions.Clear();
                foreach (var (tier, name) in staged)
                {
                    _tiers.Add(tier);
                    NoteModelOptions.Add(name);
                }
            }

            NoteModelStatus = _tiers.Count <= 1 ? "Only one model installed" : "";
            // Preference for an unstaged tier: the engine stayed on the default,
            // and the control shows that
            if (!_tiers.Contains(_noteTier) && _tiers.Contains("default"))
            {
                NoteModelStatus = "Saved model not installed; using the default";
                _noteTier = "default";
                PersistTier();
            }

            NoteModelIndex = _tiers.IndexOf(_noteTier);
            _populating = false;
            NoteModelEnabled = _tiers.Count > 1;
        }
        catch (Exception)
        {
            _populating = false;
        }
    }

    private string NameOf(string tier)
    {
        var index = _tiers.IndexOf(tier);
        return index >= 0 && index < NoteModelOptions.Count ? NoteModelOptions[index] : tier;
    }

    partial void OnNoteModelIndexChanged(int value)
    {
        if (_reverting || _initialising || _populating || value < 0 || value >= _tiers.Count)
        {
            return;
        }

        var tier = _tiers[value];
        if (tier == _noteTier)
        {
            return;
        }

        // The switch ends the resident model; a consultation needs it
        if (_session?.ConsultationActive == true)
        {
            _reverting = true;
            NoteModelIndex = _tiers.IndexOf(_noteTier);
            _reverting = false;
            _status?.Append("finish the consultation before changing the note model");
            return;
        }

        _revertTier = _noteTier;
        _noteTier = tier;
        PersistTier();
        NoteModelEnabled = false;
        NoteModelStatus = "Loading";
        _status?.Append($"Loading {NameOf(tier)}", busy: true);
        _ = SendTierAsync(tier);
    }

    private void PersistTier()
    {
        if (_preferences is not null)
        {
            _preferences.NoteTier = _noteTier;
            _preferences.Save();
        }
    }

    private async Task SendTierAsync(string tier)
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            var reply = await _client.SetNoteTierAsync(tier).ConfigureAwait(true);
            // Already resident (the same tier after a restart): nothing to wait for
            if (reply.State == "ready")
            {
                ApplyNoteModel(reply.State, reply.Tier, firstUse: false, detail: "");
            }
        }
        catch (Exception e)
        {
            RevertTier(e.Message);
        }
    }

    // A refused or failed switch reverts to the previous tier, once; the
    // engine is told
    private void RevertTier(string reason)
    {
        var back = _revertTier;
        _revertTier = null;
        NoteModelStatus = $"Could not switch: {reason}";
        _status?.Append($"Could not switch note model: {reason}");
        if (back is null)
        {
            NoteModelEnabled = _tiers.Count > 1;
            return;
        }

        _noteTier = back;
        PersistTier();
        _reverting = true;
        NoteModelIndex = _tiers.IndexOf(back);
        _reverting = false;
        _ = SendTierAsync(back);
    }

    private void OnNoteModel(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        ApplyNoteModel(
            parameters.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
            parameters.TryGetProperty("tier", out var t) ? t.GetString() ?? "" : "",
            parameters.TryGetProperty("firstUse", out var f) && f.GetBoolean(),
            parameters.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "");
    }

    private void ApplyNoteModel(string state, string tier, bool firstUse, string detail)
    {
        switch (state)
        {
            case "loading":
                NoteModelEnabled = false;
                NoteModelStatus = firstUse
                    ? "Preparing for this computer, a few minutes the first time"
                    : "Loading";
                break;
            case "ready":
                _revertTier = null;
                NoteModelEnabled = _tiers.Count > 1;
                NoteModelStatus = "";
                // The engine is authoritative about what is resident
                if (_tiers.Contains(tier) && tier != _noteTier)
                {
                    _noteTier = tier;
                    PersistTier();
                    _reverting = true;
                    NoteModelIndex = _tiers.IndexOf(tier);
                    _reverting = false;
                }

                break;
            case "failed":
                if (tier == _noteTier)
                {
                    RevertTier(detail);
                }
                else
                {
                    NoteModelStatus = $"Load failed: {detail}";
                }

                break;
            default:
                break;
        }
    }

    // ---- guidance corpora -------------------------------------------------

    /// <summary>The corpora the engine has, refused ones included.</summary>
    public ObservableCollection<CorpusRow> GuidanceCorpora { get; } = [];

    /// <summary>Why nothing is listed, empty when corpora are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuidanceCaptionVisible), nameof(GuidanceInstalledVisible))]
    public partial string GuidanceCaption { get; set; } = "";

    public bool GuidanceCaptionVisible => GuidanceCaption.Length > 0;

    /// <summary>Hidden when nothing is installed, so no empty card shows.</summary>
    public bool GuidanceInstalledVisible => GuidanceCorpora.Count > 0 || GuidanceCaption.Length > 0;

    private async Task LoadGuidanceCorporaAsync()
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            ApplyGuidanceCorpora(await _client.GuidanceCorporaAsync().ConfigureAwait(true));
        }
        catch (Exception)
        {
            GuidanceCorpora.Clear();
            GuidanceCaption = "Unavailable";
        }
    }

    private void ApplyGuidanceCorpora(CorporaStatus reply)
    {
        GuidanceCorpora.Clear();
        foreach (var corpus in reply.Corpora)
        {
            GuidanceCorpora.Add(RowFrom(corpus) with { Divided = GuidanceCorpora.Count > 0 });
        }

        var detail = reply.Detail ?? "";
        GuidanceCaption = reply.State switch
        {
            "loading" => "Loading",
            "unavailable" => detail.Length > 0 ? $"Unavailable: {detail}" : "Unavailable",
            _ => "",
        };
        OnPropertyChanged(nameof(GuidanceInstalledVisible));
    }

    // A refused corpus keeps its place, so unused guidance stays visible
    private static CorpusRow RowFrom(CorpusInfo corpus)
    {
        var refused = corpus.Unavailable ?? "";
        if (refused.Length > 0)
        {
            return new CorpusRow(corpus.Id, $"Not used: {refused}", "", true);
        }

        // The licence is not shown: the attribution line is what it asks for
        var parts = new List<string>();
        if (corpus.Chunks > 0)
        {
            parts.Add($"{corpus.Chunks:N0} passages");
        }

        var built = GuidanceCard.ShortDate(corpus.BuiltAt ?? "");
        if (built.Length > 0)
        {
            parts.Add(built);
        }

        return new CorpusRow(corpus.Name, string.Join(" · ", parts), corpus.Attribution ?? "", false);
    }

    // ---- added documents --------------------------------------------------

    /// <summary>The documents in the guidelines folder: the batch in progress, then by name.</summary>
    public ObservableCollection<DocumentRow> Documents { get; } = [];

    /// <summary>The guidelines folder the engine watches.</summary>
    [ObservableProperty]
    public partial string GuidelinesFolder { get; private set; } = "";

    /// <summary>The folder and its parent were out of reach at the last scan.</summary>
    [ObservableProperty]
    public partial bool FolderMissing { get; private set; }

    /// <summary>The folder sits inside OneDrive, so the documents sync to the cloud.</summary>
    [ObservableProperty]
    public partial bool FolderInOneDrive { get; private set; }

    /// <summary>What the last add skipped, or why nothing could be added.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentsCaptionVisible))]
    public partial string DocumentsCaption { get; set; } = "";

    public bool DocumentsCaptionVisible => DocumentsCaption.Length > 0;

    /// <summary>"32 documents · all ready", or what is still being read or could not be.</summary>
    public string DocumentsSummary
    {
        get
        {
            var working = Documents.Count(r => r.Working);
            var failed = Documents.Count(r => r.Failed);
            var state = (working, failed) switch
            {
                (0, 0) => "all ready",
                (_, 0) => $"reading {working}",
                (0, _) => $"{failed} could not be read",
                _ => $"reading {working}, {failed} could not be read",
            };
            return $"{GuidanceCard.Count(Documents.Count, "document")} · {state}";
        }
    }

    /// <summary>The list is closed unless it was left open. Work or a failure opens it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentsCollapsed))]
    public partial bool DocumentsExpanded { get; set; }

    public bool DocumentsCollapsed => !DocumentsExpanded;

    [RelayCommand]
    private void ToggleDocuments() => DocumentsExpanded = !DocumentsExpanded;

    private bool _documentsNeededAttention;
    private bool _openingForAttention;

    partial void OnDocumentsExpandedChanged(bool value)
    {
        if (!_initialising && !_openingForAttention && _preferences is not null)
        {
            _preferences.DocumentsExpanded = value;
            _preferences.Save();
        }
    }

    public bool DocumentsPresent => Documents.Count > 0;

    [RelayCommand]
    private void OpenFolder()
    {
        if (GuidelinesFolder.Length > 0)
        {
            _launcher?.RevealFolder(GuidelinesFolder);
        }
    }

    [RelayCommand]
    private async Task AddDocuments()
    {
        if (_picker is null)
        {
            return;
        }

        var paths = await _picker.PickFilesAsync([".pdf", ".txt", ".md"]).ConfigureAwait(true);
        if (paths.Count > 0)
        {
            await AddPathsAsync(paths).ConfigureAwait(true);
        }
    }

    private async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            var added = await _client.AddDocumentsAsync(paths).ConfigureAwait(true);
            foreach (var document in added.Documents)
            {
                Upsert(document);
            }

            DocumentsCaption = SkippedCaption(added.Skipped);
        }
        catch (Exception e)
        {
            DocumentsCaption = "The documents could not be added";
            _status?.Log($"guidance/documents/add failed: {e.Message}");
        }
    }

    // Skipped files become a count under the add row
    private static string SkippedCaption(IReadOnlyList<SkippedFile> skipped)
    {
        if (skipped.Count == 0)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var group in skipped.GroupBy(s => s.Reason ?? ""))
        {
            var n = group.Count();
            parts.Add(group.Key switch
            {
                "unsupported" => $"{n} skipped, not PDF or text",
                "noSpace" => $"{n} skipped, not enough free space",
                _ => $"{n} could not be read",
            });
        }

        return string.Join(" · ", parts);
    }

    private async Task LoadDocumentsAsync()
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            var list = await _client.ListDocumentsAsync().ConfigureAwait(true);
            GuidelinesFolder = list.Folder ?? "";
            FolderMissing = !list.Found;
            FolderInOneDrive = InOneDrive(GuidelinesFolder, OneDriveRoots());
            var unsupported = list.Unsupported;
            Documents.Clear();
            foreach (var document in list.Documents)
            {
                Upsert(document);
            }

            DocumentsCaption = unsupported == 0 ? ""
                : unsupported == 1 ? "1 other file is not searched, not PDF or text"
                : $"{unsupported} other files are not searched, not PDF or text";
        }
        catch (Exception)
        {
            Documents.Clear();
        }

        OnPropertyChanged(nameof(DocumentsPresent));
        RefreshBatch();
    }

    // A row keeps its place while it works, then sorts by name below the batch
    private void Upsert(DocumentInfo document)
    {
        var row = Documents.FirstOrDefault(r => r.Id == document.Id);
        if (document.State == "removed")
        {
            if (row is not null)
            {
                Documents.Remove(row);
            }
        }
        else if (row is null)
        {
            row = new DocumentRow(document, RemoveDocumentCommand);
            Documents.Insert(Place(row), row);
        }
        else
        {
            var wasWorking = row.Working;
            row.Apply(document);
            if (wasWorking && !row.Working)
            {
                Documents.Remove(row);
                Documents.Insert(Place(row), row);
            }
        }

        OnPropertyChanged(nameof(DocumentsPresent));
        RefreshBatch();
    }

    // Working rows first in arrival order, then unreadable ones, then the rest, each by name
    private int Place(DocumentRow row)
    {
        static int Group(DocumentRow r) => r.Working ? 0 : r.Failed ? 1 : 2;
        var i = 0;
        while (i < Documents.Count
            && (Group(Documents[i]) < Group(row)
                || (Group(Documents[i]) == Group(row)
                    && (row.Working || string.Compare(
                        Documents[i].Name, row.Name, StringComparison.OrdinalIgnoreCase) < 0))))
        {
            i++;
        }

        return i;
    }

    private void ApplyProgress(JsonElement progress)
    {
        if (progress.TryGetProperty("id", out var id) && id.TryGetInt64(out var value))
        {
            Documents.FirstOrDefault(r => r.Id == value)?.ApplyProgress(progress);
        }
    }

    private static readonly string[] OneDriveVariables =
        ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    private static IEnumerable<string> OneDriveRoots() =>
        OneDriveVariables.Select(Environment.GetEnvironmentVariable)
            .Where(root => !string.IsNullOrEmpty(root))
            .Select(root => root!);

    // A folder under a OneDrive root syncs to the cloud, which the caption states
    public static bool InOneDrive(string folder, IEnumerable<string> roots) =>
        folder.Length > 0
        && roots.Any(root => folder.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    // The summary follows every change. The first sign of work or a failure opens
    // the list once, without making that the remembered choice
    private void RefreshBatch()
    {
        OnPropertyChanged(nameof(DocumentsSummary));
        var attention = Documents.Any(r => r.Working || r.Failed);
        if (attention && !_documentsNeededAttention && !DocumentsExpanded)
        {
            _openingForAttention = true;
            DocumentsExpanded = true;
            _openingForAttention = false;
        }

        _documentsNeededAttention = attention;
    }

    /// <summary>Remove sends the file to the Recycle Bin, so it always confirms.</summary>
    [RelayCommand]
    private async Task RemoveDocument(DocumentRow? row)
    {
        if (row is null || _client is null || !_client.Connected)
        {
            return;
        }

        if (_dialogs is not null && !await _dialogs.ConfirmAsync($"Remove {row.Name}?",
                "The file is moved to the Recycle Bin and no longer searched. To keep the file, "
                + "move it out of the folder instead. Guidance already saved with a consultation "
                + "is unchanged.", "Remove").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await _client.RemoveDocumentAsync(row.Id).ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _status?.Log($"guidance/documents/remove failed: {e.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveAllDocuments()
    {
        if (_client is null || !_client.Connected || Documents.Count == 0)
        {
            return;
        }

        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                Documents.Count == 1 ? "Remove the document?" : $"Remove all {Documents.Count} documents?",
                "Every file in the folder is moved to the Recycle Bin and no longer searched. "
                + "Guidance already saved with consultations is unchanged.", "Remove all")
            .ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await _client.RemoveAllDocumentsAsync().ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _status?.Log($"guidance/documents/removeAll failed: {e.Message}");
        }
    }

    /// <summary>
    /// Off by default: a consultation is erased when it is left. On: the
    /// encrypted history. Applies to consultations from now on; audio is
    /// never kept either way.
    /// </summary>
    [ObservableProperty]
    public partial bool KeepConsultations { get; set; }

    // Turning the history on starts accumulating patient records, so it is
    // confirmed first. Turning it off is never gated
    partial void OnKeepConsultationsChanged(bool value)
    {
        if (_reverting || _initialising)
        {
            return;
        }

        if (value && _dialogs is not null)
        {
            _reverting = true;
            KeepConsultations = false;  // holds until the clinician confirms
            _reverting = false;
            _ = AskThenEnableAsync();
            return;
        }

        PersistKeepConsultations(value);
    }

    private async Task AskThenEnableAsync()
    {
        if (await _dialogs!.ConfirmAsync("Save consultation data?",
                "Transcripts, notes and patient sheets will be stored encrypted on this device."
                + "\n\nContinue only if you have the necessary consent and approval.", "Turn on")
            .ConfigureAwait(true))
        {
            _reverting = true;
            KeepConsultations = true;
            _reverting = false;
            PersistKeepConsultations(true);
        }
    }

    private void PersistKeepConsultations(bool value)
    {
        if (_preferences is not null)
        {
            _preferences.KeepConsultations = value;
            _preferences.Save();
        }
    }

    [ObservableProperty]
    public partial string Heading { get; set; } = "Settings";

    /// <summary>"system", "light" or "dark"; the shell applies it live.</summary>
    [ObservableProperty]
    public partial string Theme { get; set; } = "system";

    partial void OnThemeChanged(string value)
    {
        OnPropertyChanged(nameof(ThemeIndex));
        if (_initialising)
        {
            return;
        }

        _theme?.Apply(value);
        if (_preferences is not null)
        {
            _preferences.Theme = value;
            _preferences.Save();
        }
    }

    public IReadOnlyList<string> ThemeOptions { get; } = ["System default", "Light", "Dark"];

    /// <summary>Theme as the Appearance control's selection, same order.</summary>
    public int ThemeIndex
    {
        get => Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        set => Theme = value switch { 1 => "light", 2 => "dark", _ => "system" };
    }

    /// <summary>Shows the replay tray. A developer control, never clinical.</summary>
    [ObservableProperty]
    public partial bool DemoTrayEnabled { get; set; }

    partial void OnDemoTrayEnabledChanged(bool value)
    {
        if (!_initialising && _preferences is not null)
        {
            _preferences.DemoTrayEnabled = value;
            _preferences.Save();
        }
    }

    /// <summary>Demo mode: Record plays a saved run back. A developer control.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DemoTrackOptions))]
    [NotifyPropertyChangedFor(nameof(DemoTrackIndex))]
    public partial bool DemoModeEnabled { get; set; }

    partial void OnDemoModeEnabledChanged(bool value)
    {
        if (!_initialising && _demo is not null)
        {
            _demo.Enabled = value;
        }
    }

    /// <summary>The tracks with a saved run, as the picker's items.</summary>
    public IReadOnlyList<string> DemoTrackOptions => _demo?.Tracks ?? [];

    public bool DemoTracksAvailable => DemoTrackOptions.Count > 0;

    public string DemoModeCaption => DemoTracksAvailable
        ? "Record plays the chosen saved run back in seconds; nothing is transcribed or written"
        : "No saved runs yet: record them with tools/demo/record_masters.py";

    /// <summary>The chosen track as the picker's selection; unknown falls back to the first.</summary>
    public int DemoTrackIndex
    {
        get => Math.Max(0, DemoTrackOptions.ToList().IndexOf(_demo?.Track ?? ""));
        set
        {
            if (_demo is not null && value >= 0 && value < DemoTrackOptions.Count)
            {
                _demo.Track = DemoTrackOptions[value];
            }
        }
    }

    /// <summary>
    /// Seed data: a year of sample consultations with reflections. A developer control.
    /// </summary>
    [ObservableProperty]
    public partial bool SeedDataEnabled { get; set; }

    partial void OnSeedDataEnabledChanged(bool value)
    {
        if (_initialising)
        {
            return;
        }

        if (_preferences is not null)
        {
            _preferences.SeedDataEnabled = value;
            _preferences.Save();
        }

        if (!_seedFollowsStore)
        {
            _ = ApplySeedDataAsync(value);
        }
    }

    // Set while the switch is aligned to the store, so the engine is not asked again
    private bool _seedFollowsStore;

    /// <summary>Erases every stored consultation, seeded or real.</summary>
    [RelayCommand]
    private async Task DeleteAllConsultations()
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        if (_session?.ConsultationActive == true)
        {
            _status?.Append("finish the consultation before deleting stored data");
            return;
        }

        if (_dialogs is not null && !await _dialogs.ConfirmAsync("Delete all consultation data?",
                "Every stored consultation on this device is erased: transcripts, notes, patient "
                + "sheets and appraisal reflections. Your guideline documents are kept. This cannot "
                + "be undone.", "Delete all").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var removed = await _client.DeleteAllSessionsAsync().ConfigureAwait(true);
            _status?.Append(removed == 1 ? "1 consultation deleted" : $"{removed} consultations deleted");
            // The seed was erased too; the switch follows, and switching on reseeds
            _seedFollowsStore = true;
            SeedDataEnabled = false;
            _seedFollowsStore = false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not delete: {e.Message}");
        }
    }

    // On seeds, a no-op when already seeded; off clears
    private async Task ApplySeedDataAsync(bool enabled)
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            var count = await (enabled ? _client.SeedDemoAsync() : _client.ClearDemoAsync())
                .ConfigureAwait(true);
            if (count > 0)
            {
                _status?.Append(enabled
                    ? $"{count} sample consultations added"
                    : $"{count} sample consultations removed");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"seed data: {e.Message}");
        }
    }

    /// <summary>The Developer tools group is closed unless it was left open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeveloperToolsCollapsed))]
    public partial bool DeveloperToolsExpanded { get; set; }

    public bool DeveloperToolsCollapsed => !DeveloperToolsExpanded;

    [RelayCommand]
    private void ToggleDeveloperTools() => DeveloperToolsExpanded = !DeveloperToolsExpanded;

    partial void OnDeveloperToolsExpandedChanged(bool value)
    {
        if (!_initialising && _preferences is not null)
        {
            _preferences.DeveloperToolsExpanded = value;
            _preferences.Save();
        }
    }

    /// <summary>Shows the status-bar model and memory chips. For testing.</summary>
    [ObservableProperty]
    public partial bool ShowPerformanceMetrics { get; set; }

    /// <summary>
    /// Dev builds only: include the local research corpus, the NICE demo. Hidden in a
    /// release build.
    /// </summary>
    public bool ResearchToggleVisible { get; } =
#if DEBUG
        true;
#else
        false;
#endif

    [ObservableProperty]
    public partial bool IncludeResearchGuidance { get; set; }

    partial void OnIncludeResearchGuidanceChanged(bool value)
    {
        if (_initialising)
        {
            return;
        }

        if (_preferences is not null)
        {
            _preferences.IncludeResearchGuidance = value;
            _preferences.Save();
        }

        _ = ApplyResearchAsync(value);
    }

    // The engine reloads its corpora live and announces the change, so the
    // list follows without a restart
    private async Task ApplyResearchAsync(bool include)
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            await _client.SetResearchGuidanceAsync(include).ConfigureAwait(true);
        }
        catch (Exception e)
        {
            _status?.Log($"guidance/research failed: {e.Message}");
        }
    }

    partial void OnShowPerformanceMetricsChanged(bool value)
    {
        if (_initialising)
        {
            return;
        }

        if (_status is not null)
        {
            _status.MetricsVisible = value;
        }

        if (_preferences is not null)
        {
            _preferences.ShowPerformanceMetrics = value;
            _preferences.Save();
        }
    }

    /// <summary>Local performance collection; numbers and device names only.</summary>
    [ObservableProperty]
    public partial bool CollectPerformanceData { get; set; }

    partial void OnCollectPerformanceDataChanged(bool value)
    {
        if (!_initialising && _preferences is not null)
        {
            _preferences.CollectPerformanceData = value;
            _preferences.Save();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportDescription))]
    public partial string ExportResult { get; private set; } = "";

    /// <summary>The Export row's line: the last outcome once there is one.</summary>
    public string ExportDescription =>
        ExportResult.Length > 0 ? ExportResult : "Saves the report as an HTML file";

    /// <summary>One self-contained HTML file: readable, emailable, parseable.</summary>
    [RelayCommand]
    private async Task ExportPerformanceReport()
    {
        if (_machine is null || _metrics is null || !File.Exists(_metrics.Path))
        {
            ExportResult = "no performance data collected yet";
            return;
        }

        try
        {
            var html = ReportBuilder.Build(
                _machine.Describe(), File.ReadAllLines(_metrics.Path), DateTimeOffset.UtcNow);
            var suggested = $"ambient-perf-{Environment.MachineName}-{DateTime.Now:yyyyMMdd}.html";
            var path = _picker is not null
                ? await _picker.PickSaveAsync(suggested, "HTML report", ".html").ConfigureAwait(true)
                : Path.Combine(_exportDirectory, suggested);
            if (path is null)
            {
                return;  // cancelled: no file, no caption
            }

            File.WriteAllText(path, html);
            ExportResult = $"saved {path}";
        }
        catch (Exception e)
        {
            ExportResult = $"export failed: {e.Message}";
        }
    }

    /// <summary>Runs transcription on the NPU; the engine restarts to apply.</summary>
    [ObservableProperty]
    public partial bool NpuTranscription { get; set; }

    partial void OnNpuTranscriptionChanged(bool value)
    {
        if (_reverting || _initialising)
        {
            return;
        }

        // A restart would kill a live session
        if (_session?.ConsultationActive == true)
        {
            _reverting = true;
            NpuTranscription = !value;
            _reverting = false;
            _status?.Append("finish the consultation before switching transcription device");
            return;
        }

        if (_preferences is not null)
        {
            _preferences.NpuTranscription = value;
            _preferences.Save();
        }

        if (_engine is not null)
        {
            _status?.Append(value
                ? "preparing the low-power model - the first switch can take a few minutes"
                : "switching speech recognition to the GPU", busy: true);
            _engine.Shutdown();
            _engine.Start();
        }
    }
}
