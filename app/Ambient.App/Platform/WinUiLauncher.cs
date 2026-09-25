using System.Diagnostics;
using Ambient.App.Core.Common;
using Ambient.App.Core.Ports;

namespace Ambient.App.Platform;

public sealed class WinUiLauncher : ILauncher
{
    // Web links only. Anything else is refused before it reaches the launcher
    public async Task<bool> OpenLinkAsync(string link)
    {
        try
        {
            return WebLinks.IsWeb(link)
                && await Windows.System.Launcher.LaunchUriAsync(new Uri(link));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task OpenFileAsync(string path)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        if (!await Windows.System.Launcher.LaunchFileAsync(file))
        {
            throw new InvalidOperationException("no PDF viewer answered");
        }
    }

    public void RevealFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // The caller's caption already says where the folder is
        }
    }
}
