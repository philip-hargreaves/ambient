namespace Ambient.App.Core.Ports;

/// <summary>Where the preferences json lives: a file in production, memory in a test.</summary>
public interface IPreferencesStore
{
    /// <summary>The stored json, or null when nothing has been saved yet.</summary>
    string? Read();

    void Write(string json);
}
