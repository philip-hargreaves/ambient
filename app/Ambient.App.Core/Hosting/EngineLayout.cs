namespace Ambient.App.Core.Hosting;

/// <summary>The engine's files beside the app, and its processes by name.</summary>
public static class EngineLayout
{
    public const string EngineExe = "ambient_engine.exe";

    public const string NoteHostExe = "ambient_note_host.exe";

    public const string EngineProcess = "ambient_engine";

    public const string NoteHostProcess = "ambient_note_host";

    public const string EngineLog = "engine.log";

    /// <summary>The per-user folder for the store, the logs and the preferences.</summary>
    public const string LocalStateFolder = "ambient";
}
