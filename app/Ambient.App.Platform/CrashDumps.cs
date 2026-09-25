using Microsoft.Win32;

namespace Ambient.App.Platform;

/// <summary>
/// WER local dumps for the engine processes: per-user, capped, and minidumps
/// only because a full dump could carry audio.
/// </summary>
public static class CrashDumps
{
    private const string LocalDumps =
        @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";

    public static void Register(RegistryKey hive, string dumpFolder, params string[] exeNames)
    {
        foreach (var exe in exeNames)
        {
            try
            {
                using var key = hive.CreateSubKey($@"{LocalDumps}\{exe}");
                key.SetValue("DumpFolder", dumpFolder, RegistryValueKind.ExpandString);
                key.SetValue("DumpCount", 3, RegistryValueKind.DWord);
                key.SetValue("DumpType", 1, RegistryValueKind.DWord);  // minidump
            }
            catch (Exception)
            {
                // Diagnostics must never block startup
            }
        }
    }

    /// <summary>Removes the entries for processes the app no longer ships.</summary>
    public static void Unregister(RegistryKey hive, params string[] exeNames)
    {
        foreach (var exe in exeNames)
        {
            try
            {
                hive.DeleteSubKeyTree($@"{LocalDumps}\{exe}", throwOnMissingSubKey: false);
            }
            catch (Exception)
            {
                // Diagnostics must never block startup
            }
        }
    }
}
