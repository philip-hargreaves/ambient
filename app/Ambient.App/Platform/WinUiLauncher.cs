using System.Diagnostics;
using Ambient.App.Core.Ports;

namespace Ambient.App.Platform;

public sealed class WinUiLauncher : ILauncher
{
    // Web links only. Anything else is refused here rather than handed to the launcher
    public async Task<bool> OpenLinkAsync(string link)
    {
        try
        {
            return Uri.TryCreate(link, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && await Windows.System.Launcher.LaunchUriAsync(uri);
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
