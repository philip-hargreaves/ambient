using Microsoft.UI.Xaml;

namespace Ambient.App.Features.Documents;

/// <summary>
/// Speaker roles to the styles that carry their colour. Each style's brush is a theme
/// resource, so the element it lands on resolves it for the theme in effect.
/// </summary>
public static class SpeakerPalette
{
    public static Style Stripe(string speaker) => Find(Role(speaker) + "SpeakerStripe");

    public static Style Name(string speaker) => Find(Role(speaker) + "SpeakerName");

    private static string Role(string speaker) => speaker switch
    {
        "doctor" => "Doctor",
        "patient" => "Patient",
        _ => "Unknown",
    };

    private static Style Find(string key) => (Style)Application.Current.Resources[key];
}
