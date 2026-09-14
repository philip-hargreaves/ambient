using Ambient.App.Core.ViewModels;

namespace Ambient.App.Views;

internal static class LinkHelper
{
    // Web links only, in the default browser; anything else is refused here
    // rather than handed to the launcher
    public static async Task OpenAsync(StatusBarViewModel status, string link)
    {
        var opened = false;
        try
        {
            opened = Uri.TryCreate(link, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception)
        {
        }

        if (!opened)
        {
            status.Append("Could not open the link - no browser answered");
        }
    }
}
