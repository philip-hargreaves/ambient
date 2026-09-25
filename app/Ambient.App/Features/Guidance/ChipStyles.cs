using Microsoft.UI.Xaml;

namespace Ambient.App.Features.Guidance;

/// <summary>
/// Chip styles by source: an added document in the on-device green, installed guidance
/// in the accent blue whoever published it. Each style's brushes are theme resources, so
/// the chip itself resolves them for the theme in effect.
/// </summary>
public static class ChipStyles
{
    public static Style Border(bool fromDocument) => Find(fromDocument ? "DocumentChip" : "ReferenceChip");

    public static Style Text(bool fromDocument) => Find(fromDocument ? "DocumentChipText" : "ReferenceChipText");

    private static Style Find(string key) => (Style)Application.Current.Resources[key];
}
