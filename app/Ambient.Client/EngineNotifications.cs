using System.Text.Json;

namespace Ambient.Client;

/// <summary>What the engine pushes, one record per notification.</summary>
public abstract record EngineNotification;

/// <summary>A streamed lane carries the model's token rate.</summary>
public interface IMetered
{
    double? TokensPerSecond { get; }
}

public sealed record AudioLevel(double Level = 0, bool Clipped = false, double? Seconds = null)
    : EngineNotification;

public sealed record SessionInterrupted(string? Reason = null, string? Detail = null)
    : EngineNotification;

public sealed record SessionProgress(string Stage = "") : EngineNotification;

public sealed record EnrolmentProgress(
    double Level = 0, double Elapsed = 0, double Speech = 0, bool Clipped = false)
    : EngineNotification;

public sealed record EnrolmentDone(bool Ok = false, string? Detail = null, double SpeechSeconds = 0)
    : EngineNotification;

/// <summary>The note lane's model: loading, ready or failed, with the tier it serves.</summary>
public sealed record NoteModelState(
    string State = "", string Tier = "", string Id = "", string? Name = null, bool FirstUse = false,
    double? Seconds = null, string? Detail = null)
    : EngineNotification;

public sealed record NotePartial(string Text = "", double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record NoteReady(string? Text = null, double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record NoteRefused(string Reason = "", bool Overridable = true) : EngineNotification;

public sealed record NoteFailed(string Detail = "failed") : EngineNotification;

public sealed record PatientPartial(string Text = "", double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record PatientReady(string? Text = null, double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record PatientFailed(string Detail = "failed") : EngineNotification;

public sealed record TranslationPartial(string Text = "", double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record TranslationReady(
    string Text = "", string Language = "", double? TokensPerSecond = null)
    : EngineNotification, IMetered;

public sealed record TranslationFailed(string? Detail = null) : EngineNotification;

public sealed record GuidanceModelChanged(string State = "", string? Detail = null)
    : EngineNotification;

/// <summary>A search came back: the note's when the record has an id, a typed query's otherwise.</summary>
public sealed record GuidanceReady(GuidanceRecord Record) : EngineNotification;

public sealed record GuidanceFailed(string? Id = null, string? Detail = null) : EngineNotification;

public sealed record GuidanceDocumentsChanged : EngineNotification;

public sealed record GuidanceDocumentChanged(DocumentInfo Document) : EngineNotification;

public sealed record GuidanceProgress(long Id = 0, string Phase = "", int Done = 0, int Total = 0)
    : EngineNotification;

public sealed record ReflectionSummaryReady(string Id = "", string Text = "") : EngineNotification;

public sealed record ReflectionSummaryFailed(string Id = "", string Detail = "")
    : EngineNotification;

public static class EngineNotifications
{
    /// <summary>
    /// The record for a wire notification, or null for a method the shell does not know or a
    /// payload it cannot read. Events whose fields are all optional fall back to an empty record.
    /// </summary>
    public static EngineNotification? Parse(string method, JsonElement parameters) => method switch
    {
        "audio.level" => Protocol.Parse<AudioLevel>(parameters),
        "session/interrupted" => Protocol.Parse<SessionInterrupted>(parameters) ?? new SessionInterrupted(),
        "session/progress" => Protocol.Parse<SessionProgress>(parameters),
        "anchor/progress" => Protocol.Parse<EnrolmentProgress>(parameters),
        "anchor/enrolled" => Protocol.Parse<EnrolmentDone>(parameters),
        "note/model" => Protocol.Parse<NoteModelState>(parameters),
        "note/partial" => Protocol.Parse<NotePartial>(parameters),
        "note/ready" => Protocol.Parse<NoteReady>(parameters) ?? new NoteReady(),
        "note/refused" => Protocol.Parse<NoteRefused>(parameters) ?? new NoteRefused(),
        "note/failed" => Protocol.Parse<NoteFailed>(parameters) ?? new NoteFailed(),
        "patient/partial" => Protocol.Parse<PatientPartial>(parameters),
        "patient/ready" => Protocol.Parse<PatientReady>(parameters) ?? new PatientReady(),
        "patient/failed" => Protocol.Parse<PatientFailed>(parameters) ?? new PatientFailed(),
        "translate/partial" => Protocol.Parse<TranslationPartial>(parameters),
        "translate/ready" => Protocol.Parse<TranslationReady>(parameters),
        "translate/failed" => Protocol.Parse<TranslationFailed>(parameters) ?? new TranslationFailed(),
        "guidance/model" => Protocol.Parse<GuidanceModelChanged>(parameters) ?? new GuidanceModelChanged(),
        "guidance/ready" => Protocol.Parse<GuidanceRecord>(parameters) is { } record
            ? new GuidanceReady(record)
            : null,
        "guidance/failed" => Protocol.Parse<GuidanceFailed>(parameters),
        "guidance/documentsChanged" => new GuidanceDocumentsChanged(),
        "guidance/document" => Protocol.Parse<DocumentInfo>(parameters) is { } document
            ? new GuidanceDocumentChanged(document)
            : null,
        "guidance/progress" => Protocol.Parse<GuidanceProgress>(parameters),
        "reflection/summary" => Protocol.Parse<ReflectionSummaryReady>(parameters),
        "reflection/summaryFailed" => Protocol.Parse<ReflectionSummaryFailed>(parameters),
        _ => null,
    };
}
