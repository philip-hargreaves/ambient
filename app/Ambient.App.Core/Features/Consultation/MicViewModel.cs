using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Common;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.Client;

namespace Ambient.App.Core.Features.Consultation;

/// <summary>The microphone picker: the engine's list, fetched fresh per
/// open. The choice is stored by id and falls back only while the device is gone.</summary>
public sealed partial class MicViewModel : ObservableObject
{
    private const string BluetoothNote = "    Bluetooth call mode - reduced recording quality";

    private readonly IEngineApi _engine;
    private readonly AppPreferences? _preferences;

    public MicViewModel(IEngineApi engine, AppPreferences? preferences = null,
        IUiDispatcher? dispatcher = null)
    {
        _engine = engine;
        _preferences = preferences;
        SelectedId = preferences?.MicId ?? "";
        // Refresh at connect so the label is right before the first open
        if (dispatcher is not null)
        {
            engine.OnConnected(dispatcher, () => _ = RefreshAsync());
        }
    }

    /// <summary>Empty means "the system default", which the engine pins.</summary>
    [ObservableProperty]
    public partial string SelectedId { get; private set; }

    public ObservableCollection<MicDevice> Devices { get; } = [];

    /// <summary>The picker's rows: one per device, the current one checked.</summary>
    public ObservableCollection<MicRow> Rows { get; } = [];

    public bool HasDevices => Devices.Count > 0;

    /// <summary>The picker's caption when the engine hears nothing.</summary>
    public string NoDevicesText { get; } = "No microphone found - connect one to record";

    /// <summary>What session/start should pin: the choice, or default.</summary>
    public string MicId => Current?.Id ?? "";

    /// <summary>Shortened only while still naming ONE device (several mics
    /// can all report "Microphone"): endpoint, +adapter, then full name.</summary>
    public string Label
    {
        get
        {
            if (Current is not { } current)
            {
                return "No microphone found";
            }

            var endpoint = Endpoint(current);
            if (Devices.Count(d => Endpoint(d) == endpoint) == 1)
            {
                return endpoint;
            }

            return Devices.Count(d => d.ShortName == current.ShortName) == 1
                ? current.ShortName
                : current.Name;
        }
    }

    /// <summary>The full name, so the trimmed label is never the only identification.</summary>
    public string FullName => Current?.Name ?? "No microphone found";

    private MicDevice? Current =>
        Devices.FirstOrDefault(d => d.Id == SelectedId)
        ?? Devices.FirstOrDefault(d => d.IsDefault)
        ?? Devices.FirstOrDefault();

    /// <summary>Asks the engine what it can hear. Called when the picker opens.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!_engine.Connected)
        {
            return;
        }

        try
        {
            var inputs = await _engine.ListAudioInputsAsync().ConfigureAwait(true);
            Devices.Clear();
            foreach (var device in inputs)
            {
                Devices.Add(new MicDevice(
                    device.Id, device.Name ?? "Microphone", device.ShortName ?? "Microphone",
                    device.IsDefault, device.Bluetooth));
            }
        }
        catch (Exception)
        {
            // A failed refresh keeps the last list. The engine still resolves
        }

        Changed();
    }

    /// <summary>The clinician's pick, kept for every future consultation.</summary>
    [RelayCommand]
    public void Select(string id)
    {
        SelectedId = id;
        if (_preferences is not null)
        {
            _preferences.MicId = id;
            _preferences.Save();
        }

        Changed();
    }

    private void Changed()
    {
        var current = MicId;
        Rows.Clear();
        foreach (var device in Devices)
        {
            Rows.Add(new MicRow(
                device.IsDefault ? $"{device.Name}  (default)" : device.Name,
                device.Bluetooth ? BluetoothNote : "",
                device.Id == current,
                device.Id));
        }

        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(FullName));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(MicId));
    }

    private static string Endpoint(MicDevice device)
    {
        var cut = device.ShortName.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? device.ShortName[..cut] : device.ShortName;
    }
}
