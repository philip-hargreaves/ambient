using System.Text.Json;
using Ambient.App.Core.Preferences;

namespace Ambient.App.Core.Features.Demo;

/// <summary>A saved run to play back: the stored consultation and its audio length.</summary>
public sealed record DemoMaster(string SessionId, double AudioSeconds);

/// <summary>
/// Demo mode: Record plays a saved run back instead of listening. A developer
/// setting; the saved runs are the ones the recorder names in the masters file.
/// </summary>
public sealed class DemoMode(AppPreferences? preferences = null, string? mastersPath = null,
    IReadOnlyList<DemoCase>? cases = null)
{
    private Dictionary<string, DemoMaster> _masters = LoadMasters(mastersPath);
    private bool _enabled = preferences?.DemoMode ?? false;
    private string _track = preferences?.DemoTrack ?? "";

    /// <summary>Written cases that can stand in as the note on a demo record.</summary>
    public IReadOnlyList<DemoCase> Cases { get; } = cases ?? DemoCases.Load();

    /// <summary>Raised when the switch or the chosen track changes.</summary>
    public event Action? Changed;

    /// <summary>Turning it on re-reads the masters, so a fresh recording needs no restart.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                if (value)
                {
                    _masters = LoadMasters(mastersPath);
                }

                Persist();
            }
        }
    }

    /// <summary>The track whose saved run plays; empty falls back to the first.</summary>
    public string Track
    {
        get => _track;
        set
        {
            if (_track != value)
            {
                _track = value;
                Persist();
            }
        }
    }

    /// <summary>Tracks with a saved run, in the file's order.</summary>
    public IReadOnlyList<string> Tracks => [.. _masters.Keys];

    /// <summary>What Record plays back; null when no saved run exists.</summary>
    public DemoMaster? Master =>
        _masters.TryGetValue(_track, out var chosen) ? chosen : _masters.Values.FirstOrDefault();

    // {"Elbow swelling": {"id": "...", "audioSeconds": 539.7}}; missing or broken means none
    private static Dictionary<string, DemoMaster> LoadMasters(string? path)
    {
        var masters = new Dictionary<string, DemoMaster>();
        if (path is null)
        {
            return masters;
        }

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var entry in json.RootElement.EnumerateObject())
            {
                var id = entry.Value.GetProperty("id").GetString();
                var seconds = entry.Value.GetProperty("audioSeconds").GetDouble();
                if (!string.IsNullOrEmpty(id) && seconds > 0)
                {
                    masters[entry.Name] = new DemoMaster(id, seconds);
                }
            }
        }
        catch (Exception)
        {
        }

        return masters;
    }

    private void Persist()
    {
        if (preferences is not null)
        {
            preferences.DemoMode = _enabled;
            preferences.DemoTrack = _track;
            preferences.Save();
        }

        Changed?.Invoke();
    }
}
