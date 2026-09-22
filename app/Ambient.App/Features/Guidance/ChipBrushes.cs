using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ambient.App.Features.Guidance;

/// <summary>
/// Chip colours by source: an added document in the on-device green, installed
/// guidance in the accent blue whoever published it.
/// </summary>
public static class ChipBrushes
{
    public static Brush Text(string source) =>
        Find(source == "upload" ? "OnDeviceBrush" : "ReferenceChipBrush");

    public static Brush Fill(string source) =>
        Find(source == "upload" ? "OnDeviceSoftBrush" : "ReferenceChipSoftBrush");

    public static Brush Stroke(string source) =>
        Find(source == "upload" ? "OnDeviceChipBorderBrush" : "ReferenceChipBorderBrush");

    private static Brush Find(string key) => (Brush)Application.Current.Resources[key];
}
