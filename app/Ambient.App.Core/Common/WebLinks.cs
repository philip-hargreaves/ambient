namespace Ambient.App.Core.Common;

/// <summary>What the launcher will open in the browser: only an absolute http or https address.</summary>
public static class WebLinks
{
    public static bool IsWeb(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
