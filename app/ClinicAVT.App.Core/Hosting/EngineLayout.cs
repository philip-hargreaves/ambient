namespace ClinicAVT.App.Core.Hosting;

/// <summary>The engine's files beside the app, and its processes by name.</summary>
public static class EngineLayout
{
    public const string EngineExe = "clinicavt_engine.exe";

    public const string NoteHostExe = "clinicavt_note_host.exe";

    public const string EngineProcess = "clinicavt_engine";

    public const string NoteHostProcess = "clinicavt_note_host";

    public const string EngineLog = "engine.log";

    /// <summary>The per-user folder for the store, the logs and the preferences.</summary>
    public const string LocalStateFolder = "ClinicAVT";
}
