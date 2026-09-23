using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.System.Power;
using Ambient.App.Core.Metrics;

namespace Ambient.App.Platform;

/// <summary>The power slider's overlay from the registry and the mains state from Win32.</summary>
public static class PowerStateReader
{
    private const string OverlayKey =
        @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public static PowerState Read()
    {
        var onMains = true;
        var status = new SYSTEM_POWER_STATUS();
        if (PInvoke.GetSystemPowerStatus(out status))
        {
            onMains = status.ACLineStatus != 0;
        }

        var overlay = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(OverlayKey);
            overlay = key?.GetValue(onMains ? "ActiveOverlayAcPowerScheme" : "ActiveOverlayDcPowerScheme")
                as string ?? "";
        }
        catch (Exception)
        {
        }

        return new PowerState(PowerState.ModeName(overlay), onMains);
    }
}
