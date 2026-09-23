using Ambient.App.Core.Features.Appraisal;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Features.Appraisal;

/// <summary>Saves one reflection as text, warning about identifiers first.</summary>
internal static class ReflectionSave
{
    public static async Task SaveAsync(
        IDialogService dialogs, IFilePicker picker, StatusBarViewModel status, string text,
        string title)
    {
        var warning = IdentifierCheck.Describe(text);
        if (warning.Length > 0 && !await dialogs.ConfirmAsync("Check before saving",
                warning + "\n\nChange the wording, or save as it is.", "Save anyway", "Go back"))
        {
            return;
        }

        var path = await picker.PickSaveAsync(FileName(title), "Plain text", ".txt");
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, text);
        status.Append($"Reflection saved to {Path.GetFileName(path)}");
    }

    private static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        return name.Length > 0 ? "reflection - " + name : "reflection";
    }
}
