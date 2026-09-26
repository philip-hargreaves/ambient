namespace ClinicAVT.App.Core.Common;

/// <summary>The launcher opens only absolute http and https links in the browser.</summary>
public static class WebLinks
{
    public static bool IsWeb(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
