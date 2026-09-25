using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Preferences;

/// <summary>
/// One small json document of app preferences. Absent means defaults, unreadable means
/// defaults and a log line. Values the shell cannot render or the engine would refuse never
/// leave the load boundary.
/// </summary>
public sealed class AppPreferences(IPreferencesStore store, ILogger? logger = null)
{
    /// <summary>The json as written: named fields, a schema version, nothing positional.</summary>
    private sealed record PreferencesFile
    {
        public int SchemaVersion { get; init; } = CurrentSchema;

        public bool DemoTrayEnabled { get; init; }

        public bool DemoMode { get; init; }

        public string? DemoTrack { get; init; }

        public bool SeedDataEnabled { get; init; }

        public bool NpuTranscription { get; init; }

        public bool CollectPerformanceData { get; init; }

        public bool KeepConsultations { get; init; }

        public bool ShowPerformanceMetrics { get; init; }

        public bool IncludeResearchGuidance { get; init; }

        public string? MicId { get; init; }

        public string? Theme { get; init; }

        public string? NoteStyle { get; init; }

        public string? NoteDetail { get; init; }

        public string? NoteTier { get; init; }
    }

    public const int CurrentSchema = 1;

    /// <summary>The values the shell can render or the engine accepts, each with its default first.</summary>
    public static readonly IReadOnlyList<string> Themes = ["system", "light", "dark"];

    /// <summary>The note model tiers the engine's store can resolve, in ladder order.</summary>
    public static readonly IReadOnlyList<string> NoteTiers = ["constrained", "default", "accuracy"];

    public AppPreferences(string path, ILogger? logger = null)
        : this(new FilePreferencesStore(path), logger)
    {
    }

    /// <summary>Raised after every save, so a page can follow a preference it does not own.</summary>
    public event Action? Saved;

    public bool DemoTrayEnabled { get; set; }

    /// <summary>Demo mode: Record plays a saved run back. A developer control.</summary>
    public bool DemoMode { get; set; }

    /// <summary>The track whose saved run demo mode plays. Empty means the first.</summary>
    public string DemoTrack { get; set; } = "";

    /// <summary>Seed data in the store, off by default.</summary>
    public bool SeedDataEnabled { get; set; }

    public bool NpuTranscription { get; set; }

    public bool CollectPerformanceData { get; set; }

    /// <summary>
    /// Off by default: the app saves nothing beyond the consultation unless
    /// the clinician opts in. Applies to consultations from now on.
    /// </summary>
    public bool KeepConsultations { get; set; }

    /// <summary>Off by default: the status-bar chips are for testing.</summary>
    public bool ShowPerformanceMetrics { get; set; }

    /// <summary>
    /// Dev builds only: search corpora marked research (the local NICE demo). Passed to
    /// the engine as --include-research on launch. Never shown or set in a release build.
    /// </summary>
    public bool IncludeResearchGuidance { get; set; }

    /// <summary>The chosen microphone's endpoint id. Empty means the default.</summary>
    public string MicId { get; set; } = "";

    /// <summary>"system" follows the OS, while "light" and "dark" override it.</summary>
    public string Theme { get; set; } = Themes[0];

    public string NoteStyle { get; set; } = NoteOptions.DefaultStyle.Value;

    public string NoteDetail { get; set; } = NoteOptions.DefaultDetail.Value;

    /// <summary>
    /// Which note model the engine loads, as a role ("default", "accuracy",
    /// "constrained"). The engine's store resolves it to a model.
    /// </summary>
    public string NoteTier { get; set; } = "default";

    public static AppPreferences Load(string path, ILogger? logger = null) =>
        Load(new FilePreferencesStore(path), logger);

    public static AppPreferences Load(IPreferencesStore store, ILogger? logger = null)
    {
        var preferences = new AppPreferences(store, logger);
        PreferencesFile? stored;
        try
        {
            var json = store.Read();
            if (json is null)
            {
                return preferences;
            }

            stored = JsonSerializer.Deserialize<PreferencesFile>(json);
        }
        catch (Exception e)
        {
            // A corrupt document means defaults. The next save replaces it
            logger?.PreferencesUnreadable(e.Message);
            return preferences;
        }

        if (stored is null)
        {
            return preferences;
        }

        // A newer document is read for what this build knows. A save rewrites it at this schema
        preferences.DemoTrayEnabled = stored.DemoTrayEnabled;
        preferences.DemoMode = stored.DemoMode;
        preferences.DemoTrack = stored.DemoTrack ?? "";
        preferences.SeedDataEnabled = stored.SeedDataEnabled;
        preferences.NpuTranscription = stored.NpuTranscription;
        preferences.CollectPerformanceData = stored.CollectPerformanceData;
        preferences.KeepConsultations = stored.KeepConsultations;
        preferences.ShowPerformanceMetrics = stored.ShowPerformanceMetrics;
        preferences.IncludeResearchGuidance = stored.IncludeResearchGuidance;
        preferences.MicId = stored.MicId ?? "";
        preferences.Theme = Known(stored.Theme, Themes, Themes[0]);
        preferences.NoteStyle = NoteOptions.Style(stored.NoteStyle).Value;
        preferences.NoteDetail = NoteOptions.Detail(stored.NoteDetail).Value;
        preferences.NoteTier = Known(stored.NoteTier, NoteTiers, "default");
        return preferences;
    }

    public void Save()
    {
        try
        {
            store.Write(JsonSerializer.Serialize(new PreferencesFile
            {
                DemoTrayEnabled = DemoTrayEnabled,
                DemoMode = DemoMode,
                DemoTrack = DemoTrack,
                SeedDataEnabled = SeedDataEnabled,
                NpuTranscription = NpuTranscription,
                CollectPerformanceData = CollectPerformanceData,
                KeepConsultations = KeepConsultations,
                ShowPerformanceMetrics = ShowPerformanceMetrics,
                IncludeResearchGuidance = IncludeResearchGuidance,
                MicId = MicId,
                Theme = Theme,
                NoteStyle = NoteStyle,
                NoteDetail = NoteDetail,
                NoteTier = NoteTier,
            }));
        }
        catch (Exception e)
        {
            logger?.PreferencesNotSaved(e.Message);
        }

        Saved?.Invoke();
    }

    private static string Known(string? value, IReadOnlyList<string> known, string fallback) =>
        value is not null && known.Contains(value) ? value : fallback;
}
