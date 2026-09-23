using Ambient.App.Core.Features.Documents;
using Ambient.App.Core.Shell;
using Ambient.App.Tests.TestDoubles;

namespace Ambient.App.Tests.Features.Documents;

public class DocumentExportViewModelTest
{
    private static (DocumentExportViewModel Export, NoteViewModel Note, FakeClipboard Clipboard,
        FakeFilePicker Picker, StatusBarViewModel Status) Create()
    {
        var note = new NoteViewModel();
        var clipboard = new FakeClipboard();
        var picker = new FakeFilePicker();
        var status = new StatusBarViewModel();
        return (new DocumentExportViewModel(note, clipboard, picker, status), note, clipboard, picker, status);
    }

    [Fact]
    public async Task CopyPutsTheDocumentOnTheClipboardAndSaysSo()
    {
        var (export, note, clipboard, _, status) = Create();
        note.ClinicalNoteText = "the note";
        note.PatientInfoText = "the sheet";

        await export.CopyNoteCommand.ExecuteAsync(null);
        await export.CopyPatientCommand.ExecuteAsync(null);

        Assert.Equal(["the note", "the sheet"], clipboard.Copied);
        Assert.Contains("Patient note copied", status.LatestActivity);
    }

    [Fact]
    public async Task ExportWritesTheFileWithTheMarkerAndTheTranslation()
    {
        var (export, note, _, picker, status) = Create();
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            note.PatientInfoText = "take one tablet";
            note.TranslationText = "prendre un comprimé";
            note.TranslationLanguage = "French";
            picker.SavePath = Path.Combine(dir.FullName, "sheet.txt");

            await export.ExportPatientCommand.ExecuteAsync(null);

            var written = await File.ReadAllTextAsync(picker.SavePath);
            Assert.StartsWith(DocumentExport.Marker, written);
            Assert.Contains("take one tablet\n\nFrench translation\n\nprendre un comprimé", written);
            Assert.Equal(["patient-sheet.txt"], picker.SuggestedNames);
            Assert.Contains("outside the encrypted store", status.LatestActivity);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ACancelledPickerWritesNothing()
    {
        var (export, note, _, picker, status) = Create();
        note.ClinicalNoteText = "the note";
        picker.SavePath = null;

        await export.ExportNoteCommand.ExecuteAsync(null);

        Assert.Equal(["clinical-note.txt"], picker.SuggestedNames);
        Assert.Equal("", status.LatestActivity);
    }
}
