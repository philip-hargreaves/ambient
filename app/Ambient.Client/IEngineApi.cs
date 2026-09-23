using System.Text.Json;

namespace Ambient.Client;

/// <summary>
/// The engine as the shell uses it: one method per request, typed replies.
/// EngineApi is the only implementation and owns every method name, parameter
/// shape and reply shape.
/// </summary>
public interface IEngineApi
{
    /// <summary>True while a verified transport is up; requests can succeed.</summary>
    bool Connected { get; }

    event Action<bool>? ConnectedChanged;

    event Action<string, JsonElement>? NotificationReceived;

    // Engine
    Task<EngineReadiness> ReadinessAsync();

    Task<IReadOnlyList<ModelInfo>> ListModelsAsync();

    Task<EngineMetrics> MetricsAsync(TimeSpan? timeout = null);

    Task<IReadOnlyList<AudioInput>> ListAudioInputsAsync();

    // Session. Every start answers with the session id, empty when the engine sent none
    Task<string> StartSessionAsync(bool retain, string micId);

    Task<string> StartReplayAsync(bool retain, ReplayRequest replay);

    Task<string> StartPlaybackAsync(string sessionId);

    Task<string> ResumeSessionAsync(string sessionId, bool retain, ReplayRequest? replay);

    Task<string> StopSessionAsync();

    Task CancelSessionAsync();

    Task PauseSessionAsync(bool paused);

    Task MonitorSessionAsync(bool monitor);

    Task OpenSessionAsync(string id);

    Task CloseSessionAsync();

    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync();

    Task<IReadOnlyList<TranscriptTurn>> TranscriptAsync(string id);

    Task<StoredNote> StoredNoteAsync(string id);

    Task<StoredPatient> StoredPatientAsync(string id);

    /// <summary>The guidance record saved with the note, or null when there is none.</summary>
    Task<JsonElement?> StoredGuidanceAsync(string id);

    Task LabelSessionAsync(string id, string text);

    Task DeleteSessionAsync(string id);

    /// <summary>Erases every stored consultation; how many went.</summary>
    Task<int> DeleteAllSessionsAsync();

    // Notes
    Task<NoteTierState> SetNoteTierAsync(string tier);

    Task SetNoteOptionsAsync(string style, string detail);

    Task RegenerateNoteAsync(string style, string detail, bool confirmed = false);

    Task UpdateNoteAsync(string id, string text);

    Task RegeneratePatientAsync();

    Task UpdatePatientAsync(string id, string text);

    Task TranslatePatientAsync(string id, string language);

    Task<IReadOnlyList<string>> LanguagesAsync();

    // Guidance
    Task<CorporaStatus> GuidanceCorporaAsync();

    Task SearchGuidanceAsync(string id);

    Task SearchGuidanceAsync(string text, int limit);

    Task<GuidancePage> PageAsync(long document, int page, string chunkId);

    Task<DocumentList> ListDocumentsAsync();

    Task<DocumentsAdded> AddDocumentsAsync(IReadOnlyList<string> paths);

    Task RemoveDocumentAsync(long id);

    Task RemoveAllDocumentsAsync();

    /// <summary>The file's path, empty when the engine has none.</summary>
    Task<string> OpenDocumentAsync(long id);

    Task SetResearchGuidanceAsync(bool include);

    // Voice
    Task<AnchorStatus> AnchorStatusAsync();

    Task ClearAnchorAsync();

    Task StartEnrolmentAsync(double seconds, string micId);

    Task CancelEnrolmentAsync();

    Task FinishEnrolmentAsync();

    // Reflections
    Task<IReadOnlyList<ReflectionEntry>> ListReflectionsAsync();

    Task<StoredReflection> GetReflectionAsync(string id);

    Task SummariseReflectionAsync(string id);

    Task UpdateReflectionAsync(string id, string happened, string learned, string nextTime);

    Task UpdateReflectionSummaryAsync(string id, string summary);

    Task DeleteReflectionAsync(string id);

    // Demo data; how many consultations were added or removed
    Task<int> SeedDemoAsync();

    Task<int> ClearDemoAsync();
}
