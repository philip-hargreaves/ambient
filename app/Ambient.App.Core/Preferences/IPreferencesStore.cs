namespace Ambient.App.Core.Preferences;

/// <summary>Where the preferences json lives; a file in production, memory in a test.</summary>
public interface IPreferencesStore
{
    /// <summary>The stored json, or null when nothing has been saved yet.</summary>
    string? Read();

    void Write(string json);
}

public sealed class FilePreferencesStore(string path) : IPreferencesStore
{
    public string? Read() => File.Exists(path) ? File.ReadAllText(path) : null;

    public void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}

public sealed class MemoryPreferencesStore : IPreferencesStore
{
    public string? Json { get; set; }

    public string? Read() => Json;

    public void Write(string json) => Json = json;
}
