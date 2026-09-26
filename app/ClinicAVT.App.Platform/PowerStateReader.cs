using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Power;
using ClinicAVT.App.Core.Metrics;

namespace ClinicAVT.App.Platform;

/// <summary>The power slider's overlay from the registry and the mains state from Win32.</summary>
public static class PowerStateReader
{
    private const string OverlayKey =
        @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public static PowerState Read()
    {
        var onMains = !PInvoke.GetSystemPowerStatus(out var status) || status.ACLineStatus != 0;

        var overlay = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(OverlayKey);
            overlay = key?.GetValue(onMains ? "ActiveOverlayAcPowerScheme" : "ActiveOverlayDcPowerScheme")
                as string ?? "";
        }
        catch (Exception)
        {
            // An unreadable key reads as the default overlay
        }

        return new PowerState(PowerState.ModeName(overlay), onMains);
    }
}
