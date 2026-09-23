using Ambient.App.Core.Hosting;

namespace Ambient.App.Composition;

/// <summary>Where the app keeps its files: one per-user folder, identity-free since unpackaged runs have no ApplicationData.</summary>
public sealed record AppPaths(string LocalState)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        EngineLayout.LocalStateFolder));

    /// <summary>The folder the rename migration reads from.</summary>
    public string OldLocalState =>
        Path.Combine(Path.GetDirectoryName(LocalState)!, "sotto");

    public static string EngineExe => Path.Combine(AppContext.BaseDirectory, EngineLayout.EngineExe);

    public string EngineLog => Path.Combine(LocalState, EngineLayout.EngineLog);

    public string ShellLog => Path.Combine(LocalState, "shell.log");

    public string Preferences => Path.Combine(LocalState, "preferences.json");

    public string Metrics => Path.Combine(LocalState, "metrics.jsonl");

    public string Crashes => Path.Combine(LocalState, "crashes.jsonl");

    public string Dumps => Path.Combine(LocalState, "dumps");

    public string Masters => Path.Combine(LocalState, "masters.json");
}
