namespace ClinicAVT.App.Core.Ports;

public interface IPreferencesStore
{
    /// <summary>The stored json, or null when nothing has been saved yet.</summary>
    string? Read();

    void Write(string json);
}
