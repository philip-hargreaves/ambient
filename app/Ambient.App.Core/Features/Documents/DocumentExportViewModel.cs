using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;

namespace Ambient.App.Core.Features.Documents;

/// <summary>Copy and Export for the note and the patient sheet.</summary>
public sealed partial class DocumentExportViewModel(
    NoteViewModel note, IClipboard clipboard, IFilePicker picker, StatusBarViewModel status)
    : ObservableObject
{
    [RelayCommand]
    private Task CopyNote() => clipboard.CopyAsync(status, note.ClinicalNoteText, "Note");

    [RelayCommand]
    private Task ExportNote() => ExportAsync("clinical-note.txt", note.ClinicalNoteText);

    [RelayCommand]
    private Task CopyPatient() => clipboard.CopyAsync(status, note.PatientInfoText, "Patient note");

    // The sheet and its translation travel together to the patient
    [RelayCommand]
    private Task ExportPatient()
    {
        var text = note.PatientInfoText;
        if (note.TranslationText.Length > 0)
        {
            text += "\n\n" + note.TranslationCaption + "\n\n" + note.TranslationText;
        }

        return ExportAsync("patient-sheet.txt", text);
    }

    // Export is the one action that writes outside the encrypted store
    private async Task ExportAsync(string suggestedName, string text)
    {
        var path = await picker.PickSaveAsync(suggestedName, "Text file", ".txt").ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        await File.WriteAllTextAsync(path, DocumentExport.Marker + text).ConfigureAwait(true);
        status.Append($"Saved to {Path.GetFileName(path)} - outside the encrypted store");
    }
}
