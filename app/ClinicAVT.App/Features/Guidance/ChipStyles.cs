using Microsoft.UI.Xaml;

namespace ClinicAVT.App.Features.Guidance;

/// <summary>
/// Chip styles by source. Added documents are green and installed guidance is accent blue.
/// The brushes are theme resources, so each chip resolves them for its own theme.
/// </summary>
public static class ChipStyles
{
    public static Style Border(bool fromDocument) => Find(fromDocument ? "DocumentChip" : "ReferenceChip");

    public static Style Text(bool fromDocument) => Find(fromDocument ? "DocumentChipText" : "ReferenceChipText");

    private static Style Find(string key) => (Style)Application.Current.Resources[key];
}
