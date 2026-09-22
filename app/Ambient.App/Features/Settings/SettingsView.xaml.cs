using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Ambient.App.Core.Features.Consultation;
using Ambient.App.Core.Features.Settings;
using Ambient.App.Core.Ports;
using Ambient.App.Core.Shell;
using Ambient.App.Platform;
using Ambient.Client;

namespace Ambient.App.Features.Settings;

public sealed partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel, ShellViewModel shell, VoiceViewModel voice,
        IEngineClient engine, MicViewModel mic, IUiDispatcher dispatcher)
    {
        ViewModel = viewModel;
        Shell = shell;
        Voice = voice;
        InitializeComponent();
        // Forgetting the voiceprint cannot be undone, so it is asked once
        voice.ConfirmForget = () => ConfirmAsync("Forget voice enrolment?",
            "It will be learned again from your next consultation.", "Forget");
        // One reading per dialog; the outcome says whether a print was kept
        voice.RunEnrolment = async () =>
        {
            using var enrolment = new EnrolmentViewModel(engine, mic.MicId, dispatcher: dispatcher);
            var dialog = new EnrolmentDialog(enrolment) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            return await enrolment.Outcome;
        };
        voice.NotifyCommands();
        Loaded += (_, _) => _ = voice.RefreshAsync();
        viewModel.PickSavePath = suggested =>
            SavePickerHelper.PickAsync(suggested, "HTML report", ".html");
        viewModel.ConfirmDeleteAllConsultations = () => ConfirmAsync(
            "Delete all consultation data?",
            "Every stored consultation on this device is erased: transcripts, notes, patient "
            + "sheets and appraisal reflections. Your guideline documents are kept. This cannot "
            + "be undone.", "Delete all");
        viewModel.ConfirmKeepConsultations = () => ConfirmAsync("Save consultation data?",
            "Transcripts, notes and patient sheets will be stored encrypted on this device."
            + "\n\nContinue only if you have the necessary consent and approval.", "Turn on");
        viewModel.PickDocuments = async () =>
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".md");
            if (!SavePickerHelper.BindToWindow(picker))
            {
                return [];
            }

            var files = await picker.PickMultipleFilesAsync();
            return files.Select(f => f.Path).ToArray();
        };
        viewModel.RevealFolder = folder =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch
            {
                // The folder could not be opened. The Settings caption already says so
            }
        };
        viewModel.ConfirmRemoveDocument = name => ConfirmAsync($"Remove {name}?",
            "The file is moved to the Recycle Bin and no longer searched. To keep the file, "
            + "move it out of the folder instead. Guidance already saved with a consultation "
            + "is unchanged.", "Remove");
        viewModel.ConfirmRemoveAllDocuments = count => ConfirmAsync(
            count == 1 ? "Remove the document?" : $"Remove all {count} documents?",
            "Every file in the folder is moved to the Recycle Bin and no longer searched. "
            + "Guidance already saved with consultations is unchanged.", "Remove all");
    }

    public SettingsViewModel ViewModel { get; }

    public ShellViewModel Shell { get; }

    public VoiceViewModel Voice { get; }

    // Cancel is the safe default in every confirmation
    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
