using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Ambient.App.Views;

/// <summary>
/// Chip colours by corpus kind: NICE in NHS blue, an upload in the on-device green.
/// </summary>
public static class ChipBrushes
{
    public static Brush Text(string source) => Find(source switch
    {
        "nice" => "NiceChipBrush",
        "upload" => "OnDeviceBrush",
        _ => "TextFillColorSecondaryBrush",
    });

    public static Brush Fill(string source) => Find(source switch
    {
        "nice" => "NiceChipSoftBrush",
        "upload" => "OnDeviceSoftBrush",
        _ => "SubtleFillColorSecondaryBrush",
    });

    public static Brush Stroke(string source) => Find(source switch
    {
        "nice" => "NiceChipBorderBrush",
        "upload" => "OnDeviceChipBorderBrush",
        _ => "CardStrokeColorDefaultBrush",
    });

    private static Brush Find(string key) => (Brush)Application.Current.Resources[key];
}
