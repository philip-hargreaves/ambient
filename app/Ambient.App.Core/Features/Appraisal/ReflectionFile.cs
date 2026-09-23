using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Features.Appraisal;

/// <summary>Saves one reflection as text, warning about identifiers first.</summary>
public static class ReflectionFile
{
    public static async Task SaveAsync(
        IDialogService dialogs, IFilePicker picker, StatusBarViewModel status, string text,
        string title)
    {
        var warning = IdentifierCheck.Describe(text);
        if (warning.Length > 0 && !await dialogs.ConfirmAsync("Check before saving",
                warning + "\n\nChange the wording, or save as it is.", "Save anyway", "Go back")
            .ConfigureAwait(true))
        {
            return;
        }

        var path = await picker.PickSaveAsync(FileName(title), "Plain text", ".txt").ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, text).ConfigureAwait(true);
        status.Append($"Reflection saved to {Path.GetFileName(path)}");
    }

    /// <summary>"reflection - {title}" with the characters a file name cannot hold blanked.</summary>
    public static string FileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        return name.Length > 0 ? "reflection - " + name : "reflection";
    }
}
