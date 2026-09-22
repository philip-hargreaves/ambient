using Windows.Storage.Pickers;

namespace Ambient.App.Platform;

/// <summary>The pickers for everything that reads or writes outside the encrypted store.</summary>
internal static class SavePickerHelper
{
    /// <summary>
    /// The first line of an exported note or sheet. It tells a clinician not to file
    /// it as a guideline, and the engine's patient-data screen refuses any file that
    /// carries it, so an export dropped in the guidelines folder is never searched.
    /// </summary>
    public const string ExportMarker = "Ambient export - not for the guidelines folder.\n\n";

    public static async Task<string?> PickAsync(string suggestedName, string typeLabel,
        string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeChoices.Add(typeLabel, [extension]);
        if (!BindToWindow(picker))
        {
            return null;
        }

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>Unpackaged WinUI: a picker must be bound to our window handle.</summary>
    public static bool BindToWindow(object picker)
    {
        var window = App.Current.Window;
        if (window is null)
        {
            return false;
        }

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        return true;
    }
}
