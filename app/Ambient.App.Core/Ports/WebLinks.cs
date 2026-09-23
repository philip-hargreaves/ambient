namespace Ambient.App.Core.Ports;

/// <summary>What the launcher will open in the browser: an absolute http or https address, nothing else.</summary>
public static class WebLinks
{
    public static bool IsWeb(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
