using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Hosting;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Preferences;
using Ambient.App.Core.Shell;
using Ambient.Client;

namespace Ambient.App.Core.Features.Settings;

/// <summary>What the store keeps: the history switch, the erase, and the sample data.</summary>
public sealed partial class PrivacySettings : ObservableObject
{
    private readonly AppPreferences? _preferences;
    private readonly IEngineApi? _client;
    private readonly ISessionState? _session;
    private readonly StatusBarViewModel? _status;
    private readonly IDialogService? _dialogs;
    private readonly bool _initialising;
    private bool _reverting;

    // Set while the switch is aligned to the store, so the engine is not asked again
    private bool _seedFollowsStore;

    public PrivacySettings(
        AppPreferences? preferences, IEngineApi? client, ISessionState? session,
        StatusBarViewModel? status, IDialogService? dialogs)
    {
        _preferences = preferences;
        _client = client;
        _session = session;
        _status = status;
        _dialogs = dialogs;
        // Restoring saved values is not the clinician changing them
        _initialising = true;
        KeepConsultations = preferences?.KeepConsultations ?? false;
        SeedDataEnabled = preferences?.SeedDataEnabled ?? false;
        _initialising = false;
    }

    /// <summary>The engine connected: the seed follows the switch.</summary>
    public void Connected()
    {
        if (SeedDataEnabled)
        {
            _ = ApplySeedDataAsync(true);
        }
    }

    /// <summary>
    /// Off by default: a consultation is erased when it is left. On: the
    /// encrypted history. Applies to consultations from now on. Audio is
    /// never kept either way.
    /// </summary>
    [ObservableProperty]
    public partial bool KeepConsultations { get; set; }

    // Turning the history on starts accumulating patient records, so it is
    // confirmed first. Turning it off is never gated
    partial void OnKeepConsultationsChanged(bool value)
    {
        if (_reverting || _initialising)
        {
            return;
        }

        if (value && _dialogs is not null)
        {
            _reverting = true;
            KeepConsultations = false;  // holds until the clinician confirms
            _reverting = false;
            _ = AskThenEnableAsync();
            return;
        }

        PersistKeepConsultations(value);
    }

    private async Task AskThenEnableAsync()
    {
        if (await _dialogs!.ConfirmAsync("Save consultation data?",
                "Transcripts, notes and patient sheets will be stored encrypted on this device."
                + "\n\nContinue only if you have the necessary consent and approval.", "Turn on")
            .ConfigureAwait(true))
        {
            _reverting = true;
            KeepConsultations = true;
            _reverting = false;
            PersistKeepConsultations(true);
        }
    }

    private void PersistKeepConsultations(bool value)
    {
        if (_preferences is not null)
        {
            _preferences.KeepConsultations = value;
            _preferences.Save();
        }
    }

    /// <summary>Erases every stored consultation, seeded or real.</summary>
    [RelayCommand]
    private async Task DeleteAllConsultations()
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        if (_session?.ConsultationActive == true)
        {
            _status?.Append("finish the consultation before deleting stored data");
            return;
        }

        if (_dialogs is not null && !await _dialogs.ConfirmAsync("Delete all consultation data?",
                "Every stored consultation on this device is erased: transcripts, notes, patient "
                + "sheets and appraisal reflections. Your guideline documents are kept. This cannot "
                + "be undone.", "Delete all").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var removed = await _client.DeleteAllSessionsAsync().ConfigureAwait(true);
            _status?.Append(removed == 1 ? "1 consultation deleted" : $"{removed} consultations deleted");
            // The seed was erased too. The switch follows, and switching on reseeds
            _seedFollowsStore = true;
            SeedDataEnabled = false;
            _seedFollowsStore = false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"could not delete: {e.Message}");
        }
    }

    /// <summary>
    /// Seed data: a year of sample consultations with reflections. A developer control.
    /// </summary>
    [ObservableProperty]
    public partial bool SeedDataEnabled { get; set; }

    partial void OnSeedDataEnabledChanged(bool value)
    {
        if (_initialising)
        {
            return;
        }

        if (_preferences is not null)
        {
            _preferences.SeedDataEnabled = value;
            _preferences.Save();
        }

        if (!_seedFollowsStore)
        {
            _ = ApplySeedDataAsync(value);
        }
    }

    // On seeds, a no-op when already seeded. Off clears
    private async Task ApplySeedDataAsync(bool enabled)
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        try
        {
            var count = await (enabled ? _client.SeedDemoAsync() : _client.ClearDemoAsync())
                .ConfigureAwait(true);
            if (count > 0)
            {
                _status?.Append(enabled
                    ? $"{count} sample consultations added"
                    : $"{count} sample consultations removed");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _status?.Append($"seed data: {e.Message}");
        }
    }
}
