using Ambient.App.Core.Ports;

namespace Ambient.App.Core.Preferences;

public sealed class FilePreferencesStore(string path) : IPreferencesStore
{
    public string? Read() => File.Exists(path) ? File.ReadAllText(path) : null;

    public void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
